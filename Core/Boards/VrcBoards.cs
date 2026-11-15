namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 13 / CPROM.
    /// Fixed 32 KB PRG with 16 KB of CHR RAM banked in two 4 KB windows.
    /// </summary>
    public sealed class CpromBoard : LatchBoard
    {
        public CpromBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping, false)
        {
        }

        public override int MapperId => 13;

        public override void Power()
        {
            base.Power();

            if (PrgBankCount16() <= 1)
            {
                Mapping.SetPrg16((uint)0x8000, 0);
                Mapping.SetPrg16((uint)0xC000, 0);
            }
            else
            {
                Mapping.SetPrg32((uint)0x8000, 0);
            }

            Sync();
        }

        protected override void Sync()
        {
            // The latch selects which 4 KB half of CHR RAM appears at $1000.
            Mapping.SetChr4(0x0000, 0);
            Mapping.SetChr4(0x1000, (uint)(LatchData & 0x03));
        }
    }

    /// <summary>
    /// Konami VRC2 / VRC4 / VRC6 (mappers 21-26).
    ///
    /// All three use the same shape: eight 1 KB CHR registers, two or three
    /// PRG registers, a mirroring register and (for VRC4/VRC6) a scanline
    /// IRQ. They differ only in which CPU address selects which register, and
    /// that mapping is expressed by <see cref="VrcWiring"/>.
    ///
    /// Reference: NESdev "VRC2 and VRC4" and "VRC6".
    /// </summary>
    public sealed class VrcBoard : BoardBase
    {
        /// <summary>How a specific VRC variant maps CPU addresses to registers.</summary>
        public enum VrcWiring
        {
            Vrc2a,
            Vrc4a,
            Vrc4b,
            Vrc4c,
            Vrc4d,
            Vrc4e,
            Vrc6a,
            Vrc6b
        }

        private readonly VrcWiring _wiring;
        private readonly int _mapperId;
        private readonly bool _hasIrq;
        private readonly bool _hasSound;

        private readonly byte[] _chr = new byte[8];
        private readonly byte[] _prg = new byte[3];

        private int _prgSwap;
        private int _irqLatch;
        private int _irqCounter;
        private int _irqControl;
        private bool _irqPending;

        public VrcBoard(CartInfo cart, CartMapping mapping, int mapperId, VrcWiring wiring)
            : base(cart, mapping)
        {
            _mapperId = mapperId;
            _wiring = wiring;
            _hasIrq = wiring != VrcWiring.Vrc2a;
            _hasSound = wiring == VrcWiring.Vrc6a || wiring == VrcWiring.Vrc6b;
        }

        public override int MapperId => _mapperId;

        /// <summary>True for VRC4/VRC6 variants.</summary>
        public bool HasIrq => _hasIrq;

        /// <summary>True for VRC6, which adds three expansion sound channels.</summary>
        public bool HasExpansionSound => _hasSound;

        public bool IrqPending => _irqPending;

        public override void Power()
        {
            base.Power();

            for (int i = 0; i < _chr.Length; i++)
                _chr[i] = 0;

            _prg[0] = 0;
            _prg[1] = 1;
            _prgSwap = 0;
            _irqLatch = 0;
            _irqCounter = 0;
            _irqControl = 0;
            _irqPending = false;

            SyncPrg();
            SyncChr();
        }

        public override void Reset()
        {
            _irqPending = false;
            SyncPrg();
            SyncChr();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            switch (_wiring)
            {
                case VrcWiring.Vrc2a:
                    WriteVrc2a(address, value);
                    return;

                case VrcWiring.Vrc4a:
                case VrcWiring.Vrc4e:
                    WriteVrc4(address, value, highBitFromA1: true);
                    return;

                case VrcWiring.Vrc4b:
                case VrcWiring.Vrc4c:
                    WriteVrc4(address, value, highBitFromA1: false);
                    return;

                case VrcWiring.Vrc4d:
                    // VRC4d swaps the CHR high bit and mirroring lines.
                    WriteVrc4(address, value, highBitFromA1: true, swapChrHigh: true);
                    return;

                case VrcWiring.Vrc6a:
                case VrcWiring.Vrc6b:
                    WriteVrc6(address, value);
                    return;
            }
        }

        public override void ClockPpu(int scanline, int cycle)
        {
            if (!_hasIrq || cycle != 0)
                return;

            if ((_irqControl & 0x02) == 0)
                return;

            _irqCounter++;

            if (_irqCounter > 0xFF)
            {
                _irqCounter = _irqLatch;
                _irqPending = true;
            }
        }

        // ---------------------------------------------------------------
        // VRC2a: CHR registers are 2 KB granular and live at $B000-$E000.
        // ---------------------------------------------------------------
        private void WriteVrc2a(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0x8000:
                    _prg[0] = (byte)(value & 0x0F);
                    SyncPrg();
                    break;

                case 0x9000:
                    SyncMirroring(value & 1);
                    break;

                case 0xA000:
                    _prg[1] = (byte)(value & 0x0F);
                    SyncPrg();
                    break;

                case 0xB000:
                    SetChrPair(0, value >> 1);
                    break;

                case 0xC000:
                    SetChrPair(2, value >> 1);
                    break;

                case 0xD000:
                    SetChrPair(4, value >> 1);
                    break;

                case 0xE000:
                    SetChrPair(6, value >> 1);
                    break;
            }
        }

        // ---------------------------------------------------------------
        // VRC4: address lines A0/A1 select the register, A2-A4 the index.
        // ---------------------------------------------------------------
        private void WriteVrc4(ushort address, byte value, bool highBitFromA1, bool swapChrHigh = false)
        {
            int index = (address >> 11) & 0x03;
            bool high = highBitFromA1 ? (address & 0x02) != 0 : (address & 0x01) != 0;

            switch (address & 0xF000)
            {
                case 0x8000:
                    if (high)
                    {
                        _prg[1] = (byte)(value & 0x1F);
                    }
                    else
                    {
                        _prg[0] = (byte)(value & 0x1F);
                    }

                    SyncPrg();
                    break;

                case 0x9000:
                    if (!high)
                    {
                        SyncMirroring(swapChrHigh ? (value >> 2) & 1 : value & 1);
                    }
                    else
                    {
                        _prgSwap = value & 0x02;
                        SyncPrg();
                    }

                    break;

                case 0xA000:
                    _chr[index * 2 + (high ? 1 : 0)] = value;
                    SyncChr();
                    break;

                case 0xB000:
                    _chr[index * 2 + (high ? 1 : 0)] = value;
                    SyncChr();
                    break;

                case 0xC000:
                    _chr[index * 2 + (high ? 1 : 0)] = value;
                    SyncChr();
                    break;

                case 0xD000:
                    _chr[index * 2 + (high ? 1 : 0)] = value;
                    SyncChr();
                    break;

                case 0xE000:
                    _chr[index * 2 + (high ? 1 : 0)] = value;
                    SyncChr();
                    break;

                case 0xF000:
                    if (!high)
                    {
                        _irqLatch = (_irqLatch & 0xF0) | (value & 0x0F);
                    }
                    else
                    {
                        _irqLatch = (_irqLatch & 0x0F) | ((value & 0x0F) << 4);
                    }

                    break;
            }
        }

        // ---------------------------------------------------------------
        // VRC6: $8000-$BFFF PRG, $B003 mirroring, $C000-$FFFF CHR.
        // ---------------------------------------------------------------
        private void WriteVrc6(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0x8000:
                    _prg[0] = (byte)(value & 0x0F);
                    SyncPrg();
                    break;

                case 0x9000:
                    _prg[1] = (byte)(value & 0x0F);
                    SyncPrg();
                    break;

                case 0xA000:
                    _prg[2] = (byte)(value & 0x0F);
                    SyncPrg();
                    break;

                case 0xB000:
                    switch (address & 0x0003)
                    {
                        case 0:
                            _chr[0] = value;
                            break;
                        case 1:
                            _chr[1] = value;
                            break;
                        case 2:
                            _chr[2] = value;
                            break;
                        case 3:
                            SyncMirroring(value & 1);
                            return;
                    }

                    SyncChr();
                    break;

                case 0xC000:
                    _chr[3 + (address & 0x0003)] = value;
                    SyncChr();
                    break;

                case 0xD000:
                    _chr[4 + (address & 0x0003)] = value;
                    SyncChr();
                    break;

                case 0xE000:
                    _chr[6 + (address & 0x0003)] = value;
                    SyncChr();
                    break;

                case 0xF000:
                    switch (address & 0x0003)
                    {
                        case 0:
                            _irqLatch = value;
                            break;
                        case 1:
                            _irqCounter = _irqLatch;
                            _irqPending = false;
                            break;
                        case 2:
                            _irqControl = value;
                            _irqPending = false;
                            break;
                    }

                    break;
            }
        }

        private void SyncMirroring(int bit)
        {
            if ((Cart.Flags6 & 0x08) != 0)
                return;

            Cart.Mirroring = bit != 0
                ? (int)MirroringMode.Horizontal
                : (int)MirroringMode.Vertical;
        }

        private void SetChrPair(int baseRegister, int bank)
        {
            _chr[baseRegister] = (byte)(bank & 0xFE);
            _chr[baseRegister + 1] = (byte)((bank & 0xFE) | 1);
            SyncChr();
        }

        private void SyncChr()
        {
            for (int i = 0; i < 8; i++)
                Mapping.SetChr1((uint)(i * 0x400), _chr[i]);
        }

        private void SyncPrg()
        {
            int last = LastPrgBank8();

            Mapping.SetPrg8((uint)0x8000, _prg[0]);

            if (_wiring == VrcWiring.Vrc6a || _wiring == VrcWiring.Vrc6b)
            {
                Mapping.SetPrg8((uint)0xA000, _prg[1]);
                Mapping.SetPrg8((uint)0xC000, _prg[2]);
                Mapping.SetPrg8((uint)0xE000, (uint)last);
                return;
            }

            // VRC2/VRC4: 16 KB switchable at $8000 and the last bank at $C000,
            // or the reverse when the swap bit is set.
            if ((_prgSwap & 0x02) != 0)
            {
                Mapping.SetPrg8((uint)0x8000, (uint)(last - 1));
                Mapping.SetPrg8((uint)0xA000, (uint)last);
                Mapping.SetPrg8((uint)0xC000, _prg[0]);
                Mapping.SetPrg8((uint)0xE000, _prg[1]);
            }
            else
            {
                Mapping.SetPrg8((uint)0x8000, _prg[0]);
                Mapping.SetPrg8((uint)0xA000, _prg[1]);
                Mapping.SetPrg8((uint)0xC000, (uint)(last - 1));
                Mapping.SetPrg8((uint)0xE000, (uint)last);
            }
        }
    }
}
