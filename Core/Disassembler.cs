using System;
using System.Collections.Generic;

namespace NesLifter.Core
{
    public sealed class Instruction
    {
        public ushort Address;
        public byte Opcode;
        public OpInfo Info;

        public int Length;
        public ushort Operand;

        public string Text;
        public OpControl Control;

        public ushort Target;
        public bool HasTarget;

        public ushort Fallthrough;
        public bool HasFallthrough;
    }

    public sealed class AnalysisResult
    {
        public SortedDictionary<ushort, Instruction> Instructions =
            new SortedDictionary<ushort, Instruction>();

        public List<ushort> Labels = new List<ushort>();
        public List<ushort> Functions = new List<ushort>();
        public List<byte> UnknownOpcodes = new List<byte>();
        public List<ushort> IndirectJumps = new List<ushort>();
        public List<ushort> DynamicTargets = new List<ushort>();

        public ushort Entry;
    }

    public sealed class Disassembler
    {
        private readonly NesRom _rom;

        private readonly Queue<ushort> _queue = new Queue<ushort>();
        private readonly Dictionary<ushort, bool> _seen = new Dictionary<ushort, bool>();

        private AnalysisResult _result;

        public List<ushort> ForcedAddresses = new List<ushort>();

        public Disassembler(NesRom rom)
        {
            _rom = rom;
        }

        public AnalysisResult Analyze()
        {
            _result = new AnalysisResult();

            _queue.Clear();
            _seen.Clear();

            ushort reset = _rom.ReadVector(0xFFFC);

            if (!IsValid(reset))
            {
                if (_rom.PrgRom.Length > 0)
                    reset = 0x8000;
                else
                    reset = 0;
            }

            _result.Entry = reset;

            if (IsValid(reset))
            {
                Enqueue(reset);
                AddFunction(reset);
            }

            ushort nmiTarget = _rom.ReadVector(0xFFFA);
            ushort irqTarget = _rom.ReadVector(0xFFFE);

            if (IsValid(nmiTarget))
            {
                Enqueue(nmiTarget);
                AddFunction(nmiTarget);
            }

            if (IsValid(irqTarget))
            {
                Enqueue(irqTarget);
                AddFunction(irqTarget);
            }

            foreach (ushort forced in ForcedAddresses)
            {
                if (!IsValid(forced))
                    continue;

                Enqueue(forced);
                AddFunction(forced);

                if (!_result.DynamicTargets.Contains(forced))
                    _result.DynamicTargets.Add(forced);
            }

            while (_queue.Count > 0)
            {
                ushort addr = _queue.Dequeue();

                if (_result.Instructions.ContainsKey(addr))
                    continue;

                int offset = _rom.AddrToOffset(addr);
                if (offset < 0)
                    continue;

                Instruction inst = Decode(addr, offset);
                _result.Instructions.Add(addr, inst);

                if (inst.Control == OpControl.Invalid &&
                    !_result.UnknownOpcodes.Contains(inst.Opcode))
                {
                    _result.UnknownOpcodes.Add(inst.Opcode);
                }

                if (inst.Control == OpControl.JmpInd &&
                    !_result.IndirectJumps.Contains(addr))
                {
                    _result.IndirectJumps.Add(addr);
                }

                if (inst.Control == OpControl.JmpInd &&
                    inst.HasTarget &&
                    !_result.DynamicTargets.Contains(inst.Target))
                {
                    _result.DynamicTargets.Add(inst.Target);
                }

                if ((inst.Control == OpControl.Jsr || inst.Control == OpControl.Jmp) &&
                    inst.HasTarget &&
                    !_result.Functions.Contains(inst.Target))
                {
                    _result.Functions.Add(inst.Target);
                }

                if (inst.HasTarget)
                    Enqueue(inst.Target);

                if (inst.HasFallthrough)
                    Enqueue(inst.Fallthrough);
            }

            RemoveBadAddresses();

            _result.Labels = new List<ushort>(_result.Instructions.Keys);
            _result.Labels.Sort();

            _result.Labels.RemoveAll(delegate (ushort a)
            {
                return a < 0x8000 || a >= 0xFFFA;
            });

            return _result;
        }

        private void AddFunction(ushort addr)
        {
            if (!_result.Functions.Contains(addr))
                _result.Functions.Add(addr);
        }

        private void RemoveBadAddresses()
        {
            List<ushort> bad = new List<ushort>();

            foreach (ushort a in _result.Instructions.Keys)
            {
                if (a < 0x8000 || a >= 0xFFFA)
                    bad.Add(a);
            }

            foreach (ushort a in bad)
            {
                _result.Instructions.Remove(a);
            }
        }

        private bool IsValid(ushort addr)
        {
            if (addr < 0x8000)
                return false;

            if (addr >= 0xFFFA)
                return false;

            return _rom.AddrToOffset(addr) >= 0;
        }

        private void Enqueue(ushort addr)
        {
            if (!IsValid(addr))
                return;

            if (_seen.ContainsKey(addr))
                return;

            _seen.Add(addr, true);
            _queue.Enqueue(addr);
        }

        private int GuessUnknownLength(byte op)
        {
            int low = op & 0x0F;

            switch (low)
            {
                case 0x00: return 1;
                case 0x01: return 2;
                case 0x02: return 1;
                case 0x03: return 2;
                case 0x04: return 2;
                case 0x05: return 2;
                case 0x06: return 2;
                case 0x07: return 2;
                case 0x08: return 1;
                case 0x09: return 2;
                case 0x0A: return 1;
                case 0x0B: return 2;
                case 0x0C: return 3;
                case 0x0D: return 3;
                case 0x0E: return 3;
                case 0x0F: return 3;
                default: return 1;
            }
        }

        private Instruction Decode(ushort addr, int offset)
        {
            Instruction inst = new Instruction();
            inst.Address = addr;

            byte[] prg = _rom.PrgRom;
            byte op = prg[offset];

            inst.Opcode = op;

            OpInfo info = Cpu6502.Table[op];

            if (info == null)
            {
                inst.Length = GuessUnknownLength(op);
                inst.Control = OpControl.Invalid;
                inst.Text = "??? $" + op.ToString("X2");

                inst.Fallthrough = (ushort)(addr + inst.Length);
                inst.HasFallthrough = IsValid(inst.Fallthrough);

                return inst;
            }

            if (offset + info.Len > prg.Length)
            {
                inst.Length = 1;
                inst.Control = OpControl.Invalid;
                inst.Info = info;
                inst.Text = info.Mn + " <truncated>";

                inst.Fallthrough = (ushort)(addr + 1);
                inst.HasFallthrough = IsValid(inst.Fallthrough);

                return inst;
            }

            inst.Info = info;
            inst.Length = info.Len;
            inst.Control = info.Ctrl;

            if (inst.Length >= 2)
                inst.Operand = prg[offset + 1];

            if (inst.Length == 3)
                inst.Operand |= (ushort)(prg[offset + 2] << 8);

            inst.Text = Format(inst);

            switch (info.Ctrl)
            {
                case OpControl.Branch:
                    {
                        sbyte rel = (sbyte)inst.Operand;

                        inst.Target = (ushort)(addr + 2 + rel);
                        inst.HasTarget = IsValid(inst.Target);

                        inst.Fallthrough = (ushort)(addr + 2);
                        inst.HasFallthrough = IsValid(inst.Fallthrough);

                        break;
                    }

                case OpControl.Jmp:
                    {
                        inst.Target = inst.Operand;
                        inst.HasTarget = IsValid(inst.Target);
                        break;
                    }

                case OpControl.JmpInd:
                    {
                        int ptrOff = _rom.AddrToOffset(inst.Operand);

                        if (ptrOff >= 0 && ptrOff + 1 < prg.Length)
                        {
                            ushort possible = (ushort)(prg[ptrOff] | (prg[ptrOff + 1] << 8));

                            if (IsValid(possible))
                            {
                                inst.Target = possible;
                                inst.HasTarget = true;
                            }
                        }

                        break;
                    }

                case OpControl.Jsr:
                    {
                        inst.Target = inst.Operand;
                        inst.HasTarget = IsValid(inst.Target);

                        inst.Fallthrough = (ushort)(addr + 3);
                        inst.HasFallthrough = IsValid(inst.Fallthrough);

                        break;
                    }

                case OpControl.Rts:
                case OpControl.Rti:
                case OpControl.Brk:
                    break;

                default:
                    {
                        if (info.Mn == "KIL")
                        {
                            inst.Fallthrough = 0;
                            inst.HasFallthrough = false;
                            break;
                        }

                        inst.Fallthrough = (ushort)(addr + inst.Length);
                        inst.HasFallthrough = IsValid(inst.Fallthrough);

                        break;
                    }
            }

            return inst;
        }

        private string Format(Instruction inst)
        {
            if (inst.Info == null)
                return "??? $" + inst.Opcode.ToString("X2");

            string mn = inst.Info.Mn.PadRight(3, ' ');

            string op;

            switch (inst.Info.Mode)
            {
                case AddrMode.Imp:
                    op = string.Empty;
                    break;

                case AddrMode.Acc:
                    op = "A";
                    break;

                case AddrMode.Imm:
                    op = "#$" + inst.Operand.ToString("X2");
                    break;

                case AddrMode.Zp:
                    op = "$" + inst.Operand.ToString("X2");
                    break;

                case AddrMode.ZpX:
                    op = "$" + inst.Operand.ToString("X2") + ",X";
                    break;

                case AddrMode.ZpY:
                    op = "$" + inst.Operand.ToString("X2") + ",Y";
                    break;

                case AddrMode.Abs:
                    op = "$" + inst.Operand.ToString("X4");
                    break;

                case AddrMode.AbsX:
                    op = "$" + inst.Operand.ToString("X4") + ",X";
                    break;

                case AddrMode.AbsY:
                    op = "$" + inst.Operand.ToString("X4") + ",Y";
                    break;

                case AddrMode.Ind:
                    op = "($" + inst.Operand.ToString("X4") + ")";
                    break;

                case AddrMode.XInd:
                    op = "($" + inst.Operand.ToString("X2") + ",X)";
                    break;

                case AddrMode.IndY:
                    op = "($" + inst.Operand.ToString("X2") + "),Y";
                    break;

                case AddrMode.Rel:
                    {
                        sbyte rel = (sbyte)inst.Operand;
                        ushort target = (ushort)(inst.Address + 2 + rel);
                        op = "$" + target.ToString("X4");
                        break;
                    }

                default:
                    op = string.Empty;
                    break;
            }

            return (mn + " " + op).TrimEnd();
        }
    }
}