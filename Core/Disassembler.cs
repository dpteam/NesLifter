using System;
using System.Collections.Generic;
using NesLifter.Core.Boards;
using NesLifter.Mappers;
namespace NesLifter.Core
{
    /// <summary>
    /// One decoded 6502 instruction.
    ///
    /// Control flow is expressed in <b>PRG ROM offsets</b>, not CPU addresses:
    /// the same byte in a switchable bank can appear at several addresses
    /// depending on the mapper state, but it is always the same code. Keying
    /// by offset is what lets one lift cover every bank of a cartridge.
    ///
    /// <see cref="Address"/> is kept only as a display hint for the generated
    /// source and the analysis report.
    /// </summary>
    public sealed class Instruction
    {
        /// <summary>Offset inside PRG ROM. The identity of this instruction.</summary>
        public int Offset;

        /// <summary>CPU address this instruction was decoded at, for display.</summary>
        public ushort Address;

        public byte Opcode;
        public OpInfo? Info;

        public int Length;
        public ushort Operand;

        public string Text = string.Empty;
        public OpControl Control;

        /// <summary>Branch/jump destination as a PRG offset, or -1.</summary>
        public int TargetOffset = -1;

        /// <summary>Fall-through destination as a PRG offset, or -1.</summary>
        public int FallthroughOffset = -1;

        /// <summary>Branch/jump destination as a CPU address, for display.</summary>
        public ushort Target;
        public bool HasTarget;

        public ushort Fallthrough;
        public bool HasFallthrough;
    }

    /// <summary>
    /// Result of a whole-cartridge analysis. All address lists hold PRG offsets.
    /// </summary>
    public sealed class AnalysisResult
    {
        public SortedDictionary<int, Instruction> Instructions =
            new SortedDictionary<int, Instruction>();

        /// <summary>Every decoded offset, sorted.</summary>
        public List<int> Labels = new List<int>();

        /// <summary>Function entry offsets (JSR/JMP targets and vectors).</summary>
        public List<int> Functions = new List<int>();

        public List<byte> UnknownOpcodes = new List<byte>();

        /// <summary>CPU addresses of indirect jump sites, for display.</summary>
        public List<ushort> IndirectJumps = new List<ushort>();

        public List<ushort> DynamicTargets = new List<ushort>();

        /// <summary>RESET vector, as a CPU address.</summary>
        public ushort Entry;

        /// <summary>RESET vector, as a PRG offset.</summary>
        public int EntryOffset = -1;

        /// <summary>NMI vector as a PRG offset, or -1.</summary>
        public int NmiOffset = -1;

        /// <summary>IRQ/BRK vector as a PRG offset, or -1.</summary>
        public int IrqOffset = -1;

        /// <summary>Number of distinct bank views that contributed code.</summary>
        public int BankViews;
    }

    /// <summary>
    /// Recursive-descent 6502 analyser.
    ///
    /// It walks the cartridge several times: once with the power-on bank
    /// layout, then once per PRG bank placed in the switchable window. Every
    /// pass contributes instructions keyed by PRG offset, so a lift covers all
    /// banks instead of only the ones visible at reset.
    /// </summary>
    public sealed class Disassembler
    {
        /// <summary>Upper bound on total instructions, to keep output sane.</summary>
        private const int MaxInstructions = 400000;

        private readonly NesRom _rom;
        private readonly ProgramImage _image;

        private AnalysisResult _result = new AnalysisResult();

        public List<ushort> ForcedAddresses = new List<ushort>();

        public Disassembler(NesRom rom)
            : this(rom, ProgramImage.Create(rom, null))
        {
        }

        public Disassembler(NesRom rom, ProgramImage image)
        {
            _rom = rom ?? throw new ArgumentNullException(nameof(rom));
            _image = image ?? throw new ArgumentNullException(nameof(image));
        }

        /// <summary>Power-on address space used for decoding.</summary>
        public ProgramImage Image => _image;

        /// <summary>
        /// When false, only the power-on bank layout is analysed. Useful for
        /// fast iteration; the full lift always uses the default.
        /// </summary>
        public bool AnalyseAllBanks { get; set; } = true;

        public AnalysisResult Analyze()
        {
            _result = new AnalysisResult();

            // ---- Pass 0: the power-on layout, seeded from the vectors ----
            List<ushort> seeds = new List<ushort>();

            ushort reset = _rom.ReadVector(0xFFFC);

            if (!IsValid(reset))
                reset = _rom.PrgRom.Length > 0 ? (ushort)0x8000 : (ushort)0;

            _result.Entry = reset;
            _result.EntryOffset = _image.ToOffset(reset);

            ushort nmi = _rom.ReadVector(0xFFFA);
            ushort irq = _rom.ReadVector(0xFFFE);

            _result.NmiOffset = _image.ToOffset(nmi);
            _result.IrqOffset = _image.ToOffset(irq);

            // Vectors are function entry points in their own right.
            if (_result.EntryOffset >= 0)
                _result.Functions.Add(_result.EntryOffset);

            if (_result.NmiOffset >= 0 && !_result.Functions.Contains(_result.NmiOffset))
                _result.Functions.Add(_result.NmiOffset);

            if (_result.IrqOffset >= 0 && !_result.Functions.Contains(_result.IrqOffset))
                _result.Functions.Add(_result.IrqOffset);

            AddSeed(seeds, reset);
            AddSeed(seeds, nmi);
            AddSeed(seeds, irq);

            foreach (ushort forced in ForcedAddresses)
            {
                if (!IsValid(forced))
                    continue;

                AddSeed(seeds, forced);

                if (!_result.DynamicTargets.Contains(forced))
                    _result.DynamicTargets.Add(forced);
            }

            RunDescent(_image, seeds, isPowerOn: true);

            // ---- Passes 1..N: every PRG bank in the switchable window ----
            if (AnalyseAllBanks)
                RunBankPasses();

            FinalizeResult();

            return _result;
        }

        /// <summary>
        /// Walks each PRG bank as if it were mapped at $8000, merging the
        /// instructions it discovers by offset.
        ///
        /// The bank size must match the mapper's own granularity: putting an
        /// 8 KB slice of a 16 KB banked board into $8000 makes $A000-$BFFF
        /// point at the wrong half, which silently corrupts the layout and
        /// makes the analyser decode data as code. Boards are therefore asked
        /// for their switchable window size, not assumed to be 8 KB.
        /// </summary>
        private void RunBankPasses()
        {
            byte[] prg = _rom.PrgRom;
            int windowBytes = SwitchableWindowBytes();

            if (windowBytes <= 0)
                return;

            int bankCount = prg.Length / windowBytes;

            if (bankCount <= 1)
                return;

            // One pass over every bank. This used to loop up to four times,
            // re-walking until nothing new appeared, because a function discovered
            // while walking bank 3 could be the only way into code in bank 1.
            //
            // That no longer helps. The loop only made progress by seeding every
            // bank at every ambiguous jump target, and that was removed: it decodes
            // overlapping regions in each view and inflates the output past the size
            // of PRG (Zelda produced 140% of its own PRG and did not finish in 20
            // minutes). Ambiguous jumps are now resolved at runtime by the
            // dispatcher instead — see MapperDescriptor.FixedPrgFloor — so a second
            // pass would re-walk the same banks and find exactly the same code.
            //
            // If cross-bank seeding comes back, restore the loop with it, otherwise
            // this is dead work that the compiler rightly flags.
            for (int bank = 0; bank < bankCount; bank++)
            {
                if (_result.Instructions.Count >= MaxInstructions)
                    return;

                WalkBank(bank, windowBytes);
            }
        }

        /// <summary>Walks one bank view and merges what it finds.</summary>
        private void WalkBank(int bank, int windowBytes)
        {
            CartMapping mapping = new CartMapping();

            try
            {
                IBoard board = BoardFactory.CreateOrDefault(_rom.Cart, mapping, out _);
                board.Power();
            }
            catch (Exception)
            {
                return;
            }

            if (windowBytes == 0x2000)
                mapping.SetPrg8(0x8000, (uint)bank);
            else if (windowBytes == 0x4000)
                mapping.SetPrg16(0x8000, (uint)bank);
            else
                mapping.SetPrg32(0x8000, (uint)bank);

            ProgramImage image = ProgramImage.Create(_rom, mapping);

            if (image.ToOffset(0x8000) < 0)
                return;

            int bankStart = bank * windowBytes;
            int bankEnd = bankStart + windowBytes;

            // Seeding every bank at $8000 is only correct for the bank that is
            // actually mapped there at power-on. For every other bank, $8000 is
            // whatever data the game happens to keep in that bank, and decoding
            // it as code invents instructions that do not exist.
            //
            // That matters more than it sounds: the invented instructions have
            // fall-throughs and branches of their own, so they chain into more
            // invented instructions, and one of them eventually lands on a byte
            // that decodes as KIL. Kirby's Adventure tripped exactly this way —
            // a phantom block in bank 5 reached a $12 and trapped the CPU two
            // frames in.
            //
            // So only the power-on bank gets the free seed. Every other bank is
            // entered through real edges (known offsets, known functions) below.
            List<ushort> seeds = new List<ushort>();
            HashSet<ushort> seedSet = new HashSet<ushort>();

            if (bank == 0)
            {
                seeds.Add(0x8000);
                seedSet.Add(0x8000);
            }

            // Seed at every known code offset that falls inside this bank. A bank
            // is entered at arbitrary points (jump tables, vector tables, calls
            // from other banks), so descending from $8000 alone finds very little.
            // Known offsets come from the power-on pass and from earlier banks.
            foreach (int offset in _result.Instructions.Keys)
            {
                if (offset < bankStart || offset >= bankEnd)
                    continue;

                ushort cpuAddress = (ushort)(0x8000 + (offset - bankStart));

                if (seedSet.Add(cpuAddress))
                    seeds.Add(cpuAddress);
            }

            foreach (int entry in _result.Functions)
            {
                if (entry < bankStart || entry >= bankEnd)
                    continue;

                ushort cpuAddress = (ushort)(0x8000 + (entry - bankStart));

                if (seedSet.Add(cpuAddress))
                    seeds.Add(cpuAddress);
            }

            RunDescent(image, seeds, isPowerOn: false);
            _result.BankViews++;
        }

        /// <summary>
        /// Size in bytes of the mapper's switchable PRG window at $8000.
        /// Returns 0 for boards whose PRG layout never changes, because those
        /// need no extra passes.
        /// </summary>
        private int SwitchableWindowBytes()
        {
            MapperDescriptor descriptor = MapperCatalog.Get(_rom.Cart.Mapper);

            return descriptor.Family switch
            {
                MapperFamily.Nrom => 0,
                MapperFamily.Cnrom => 0,
                MapperFamily.Cprom => 0,
                MapperFamily.Nina => 0,

                // 16 KB banking.
                MapperFamily.Mmc1 => 0x4000,
                MapperFamily.UxRom => 0x4000,
                MapperFamily.Camerica => 0x4000,
                MapperFamily.Quattro => 0x4000,

                // 8 KB banking.
                MapperFamily.Mmc3 => 0x2000,
                MapperFamily.Mmc2 => 0x2000,
                MapperFamily.Vrc2 => 0x2000,
                MapperFamily.Vrc4 => 0x2000,
                MapperFamily.Vrc6 => 0x2000,
                MapperFamily.Fme7 => 0x2000,

                // Whole-window banking.
                MapperFamily.AxRom => 0x8000,
                MapperFamily.GxRom => 0x8000,
                MapperFamily.ColorDreams => 0x8000,
                MapperFamily.Bnrom => 0x8000,

                _ => 0x2000
            };
        }

        /// <summary>
        /// Breadth-first recursive descent over one bank view.
        /// </summary>
        private void RunDescent(ProgramImage image, List<ushort> seeds, bool isPowerOn)
        {
            Queue<ushort> queue = new Queue<ushort>();
            HashSet<ushort> seen = new HashSet<ushort>();

            foreach (ushort seed in seeds)
            {
                if (!IsValid(image, seed) || !seen.Add(seed))
                    continue;

                queue.Enqueue(seed);
            }

            while (queue.Count > 0)
            {
                if (_result.Instructions.Count >= MaxInstructions)
                    return;

                ushort addr = queue.Dequeue();

                int offset = image.ToOffset(addr);

                if (offset < 0)
                    continue;

                if (_result.Instructions.ContainsKey(offset))
                    continue;

                Instruction inst = Decode(image, addr, offset);
                _result.Instructions.Add(offset, inst);

                if (inst.Control == OpControl.Invalid && !_result.UnknownOpcodes.Contains(inst.Opcode))
                    _result.UnknownOpcodes.Add(inst.Opcode);

                if (inst.Control == OpControl.JmpInd)
                {
                    if (!_result.IndirectJumps.Contains(addr))
                        _result.IndirectJumps.Add(addr);

                    if (inst.HasTarget && !_result.DynamicTargets.Contains(inst.Target))
                        _result.DynamicTargets.Add(inst.Target);
                }

                if ((inst.Control == OpControl.Jsr || inst.Control == OpControl.Jmp) &&
                    inst.TargetOffset >= 0 &&
                    !_result.Functions.Contains(inst.TargetOffset))
                {
                    _result.Functions.Add(inst.TargetOffset);
                }

                if (isPowerOn && inst.TargetOffset >= 0 && !_result.Functions.Contains(inst.TargetOffset))
                    _result.Functions.Add(inst.TargetOffset);

                Enqueue(image, queue, seen, inst.Target);
                Enqueue(image, queue, seen, inst.Fallthrough);
            }
        }

        private void FinalizeResult()
        {
            _result.Labels = new List<int>(_result.Instructions.Keys);
            _result.Labels.Sort();

            // The vector table is data, never code.
            _result.Labels.RemoveAll(offset =>
                offset >= _rom.PrgRom.Length - 6 || offset < 0);
        }

        private static void AddSeed(List<ushort> seeds, ushort address)
        {
            if (address != 0 && !seeds.Contains(address))
                seeds.Add(address);
        }

        private void Enqueue(ProgramImage image, Queue<ushort> queue, HashSet<ushort> seen, ushort address)
        {
            if (address == 0 || !IsValid(image, address) || !seen.Add(address))
                return;

            queue.Enqueue(address);
        }

        private bool IsValid(ushort addr)
        {
            return IsValid(_image, addr);
        }

        private static bool IsValid(ProgramImage image, ushort addr)
        {
            if (addr < 0x8000 || addr >= 0xFFFA)
                return false;

            return image.ToOffset(addr) >= 0;
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

        private Instruction Decode(ProgramImage image, ushort addr, int offset)
        {
            Instruction inst = new Instruction
            {
                Offset = offset,
                Address = addr
            };

            byte[] prg = _rom.PrgRom;
            byte op = prg[offset];

            inst.Opcode = op;

            OpInfo? info = Cpu6502.Table[op];

            if (info == null)
            {
                inst.Length = GuessUnknownLength(op);
                inst.Control = OpControl.Invalid;
                inst.Text = "??? $" + op.ToString("X2");

                inst.Fallthrough = (ushort)(addr + inst.Length);
                inst.HasFallthrough = IsValid(image, inst.Fallthrough);
                inst.FallthroughOffset = image.ToOffset(inst.Fallthrough);

                return inst;
            }

            if (offset + info.Len > prg.Length)
            {
                inst.Length = 1;
                inst.Control = OpControl.Invalid;
                inst.Info = info;
                inst.Text = info.Mn + " <truncated>";

                inst.Fallthrough = (ushort)(addr + 1);
                inst.HasFallthrough = IsValid(image, inst.Fallthrough);
                inst.FallthroughOffset = image.ToOffset(inst.Fallthrough);

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
                        inst.HasTarget = IsValid(image, inst.Target);
                        inst.TargetOffset = image.ToOffset(inst.Target);

                        inst.Fallthrough = (ushort)(addr + 2);
                        inst.HasFallthrough = IsValid(image, inst.Fallthrough);
                        inst.FallthroughOffset = image.ToOffset(inst.Fallthrough);

                        break;
                    }

                case OpControl.Jmp:
                    {
                        inst.Target = inst.Operand;
                        inst.HasTarget = IsValid(image, inst.Target);
                        inst.TargetOffset = image.ToOffset(inst.Target);
                        break;
                    }

                case OpControl.JmpInd:
                    {
                        int ptrOff = image.ToOffset(inst.Operand);

                        if (ptrOff >= 0 && ptrOff + 1 < prg.Length)
                        {
                            ushort possible = (ushort)(prg[ptrOff] | (prg[ptrOff + 1] << 8));

                            if (IsValid(image, possible))
                            {
                                inst.Target = possible;
                                inst.HasTarget = true;
                                inst.TargetOffset = image.ToOffset(possible);
                            }
                        }

                        break;
                    }

                case OpControl.Jsr:
                    {
                        inst.Target = inst.Operand;
                        inst.HasTarget = IsValid(image, inst.Target);
                        inst.TargetOffset = image.ToOffset(inst.Target);

                        inst.Fallthrough = (ushort)(addr + 3);
                        inst.HasFallthrough = IsValid(image, inst.Fallthrough);
                        inst.FallthroughOffset = image.ToOffset(inst.Fallthrough);

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
                        inst.HasFallthrough = IsValid(image, inst.Fallthrough);
                        inst.FallthroughOffset = image.ToOffset(inst.Fallthrough);

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
