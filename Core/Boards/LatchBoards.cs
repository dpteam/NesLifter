namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 7 / AxROM.
    /// One 32 KB PRG window plus single-screen mirroring chosen by the
    /// bank byte (bit 4).
    /// </summary>
    public sealed class AxRomBoard : BoardBase
    {
        private byte _latch;

        public AxRomBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 7;

        public override void Power()
        {
            base.Power();
            _latch = 0;
            Sync();
        }

        public override void Reset()
        {
            _latch = 0;
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            _latch = value;
            Sync();
        }

        private void Sync()
        {
            Mapping.SetPrg32((uint)0x8000, (uint)(_latch & 0x07));

            if ((Cart.Flags6 & 0x08) == 0)
            {
                Cart.Mirroring = (_latch & 0x10) != 0
                    ? (int)MirroringMode.OneScreenHigh
                    : (int)MirroringMode.OneScreenLow;
            }

            Mapping.SetChr8(0);
        }
    }

    /// <summary>
    /// Mapper 34 / BNROM and NINA-001.
    /// A single 32 KB PRG register; the NINA-001 variant also banks CHR in
    /// 4 KB units through a register in the $7FFD-$7FFF hole.
    /// </summary>
    public sealed class BnromBoard : BoardBase
    {
        private byte _prgBank;
        private byte _chrBank;

        public BnromBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 34;

        public override void Power()
        {
            base.Power();
            _prgBank = 0;
            _chrBank = 0;
            Sync();
        }

        public override void Reset()
        {
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            _prgBank = (byte)(value & 0x0F);
            Sync();
        }

        public override void WriteLow(ushort address, byte value)
        {
            if (address < 0x7FFD)
                return;

            _chrBank = value;
            Mapping.SetChr4(0x0000, _chrBank);
        }

        private void Sync()
        {
            Mapping.SetPrg32((uint)0x8000, _prgBank);
            Mapping.SetChr8(0);
        }
    }

    /// <summary>
    /// Mapper 11 / Color Dreams and mapper 66 / GxROM.
    /// Both take the PRG bank from bits 4-5 and the CHR bank from bits 0-1 of
    /// the same write, and both suffer from bus conflicts.
    /// </summary>
    public sealed class GxRomBoard : LatchBoard
    {
        private readonly int _mapperId;

        public GxRomBoard(CartInfo cart, CartMapping mapping, int mapperId)
            : base(cart, mapping, true)
        {
            _mapperId = mapperId;
        }

        public override int MapperId => _mapperId;

        protected override void Sync()
        {
            Mapping.SetPrg32((uint)0x8000, (uint)((LatchData >> 4) & 0x03));
            Mapping.SetChr8((uint)(LatchData & 0x03));
        }
    }

    /// <summary>
    /// Mapper 71 / Camerica (Codemasters).
    /// UxROM-style 16 KB banking plus a single-screen mirroring bit.
    /// </summary>
    public sealed class CamericaBoard : LatchBoard
    {
        public CamericaBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping, true)
        {
        }

        public override int MapperId => 71;

        protected override void Sync()
        {
            Mapping.SetPrg16((uint)0x8000, (uint)(LatchData & 0x0F));
            Mapping.SetPrg16((uint)0xC000, (uint)LastPrgBank16());

            if ((Cart.Flags6 & 0x08) == 0)
            {
                Cart.Mirroring = (LatchData & 0x10) != 0
                    ? (int)MirroringMode.OneScreenHigh
                    : (int)MirroringMode.OneScreenLow;
            }

            Mapping.SetChr8(0);
        }
    }

    /// <summary>
    /// Mappers 79 / 87 / 88: fixed PRG with 8 KB CHR banking.
    /// 79 (NINA-03/06) and 88 (Namco 118) take a mirroring bit from the bank
    /// register, 87 does not.
    /// </summary>
    public sealed class NinaBoard : BoardBase
    {
        private readonly int _mapperId;
        private readonly bool _hasMirroringBit;
        private readonly int _chrShift;

        private byte _chrBank;

        public NinaBoard(CartInfo cart, CartMapping mapping, int mapperId, bool hasMirroringBit, int chrShift)
            : base(cart, mapping)
        {
            _mapperId = mapperId;
            _hasMirroringBit = hasMirroringBit;
            _chrShift = chrShift;
        }

        public override int MapperId => _mapperId;

        public override void Power()
        {
            base.Power();
            _chrBank = 0;
            Sync();
        }

        public override void Reset()
        {
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            // Bus conflict: discrete logic boards AND the write with the byte
            // that is currently mapped at the same address.
            value &= Mapping.ReadPrg(address);

            _chrBank = value;
            Sync();
        }

        private void Sync()
        {
            if (PrgBankCount16() <= 1)
            {
                Mapping.SetPrg16((uint)0x8000, 0);
                Mapping.SetPrg16((uint)0xC000, 0);
            }
            else
            {
                Mapping.SetPrg32((uint)0x8000, 0);
            }

            Mapping.SetChr8((uint)((_chrBank >> _chrShift) & 0x0F));

            if (_hasMirroringBit && (Cart.Flags6 & 0x08) == 0)
            {
                Cart.Mirroring = (_chrBank & 0x08) != 0
                    ? (int)MirroringMode.Vertical
                    : (int)MirroringMode.Horizontal;
            }
        }
    }

    /// <summary>
    /// Mapper 232 / Codemasters Quattro.
    /// Four 128 KB blocks selected by address bits, with a 64 KB outer bank
    /// register written at $C000.
    /// </summary>
    public sealed class QuattroBoard : BoardBase
    {
        private int _block;
        private int _outer;

        public QuattroBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 232;

        public override void Power()
        {
            base.Power();
            _block = 0;
            _outer = 0;
            Sync();
        }

        public override void Reset()
        {
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address >= 0xC000)
            {
                _outer = (value >> 2) & 0x07;
            }
            else if (address >= 0x8000)
            {
                _block = (value >> 3) & 0x03;
            }

            Sync();
        }

        private void Sync()
        {
            int bank = _outer * 4 + _block * 2;
            Mapping.SetPrg16((uint)0x8000, (uint)bank);
            Mapping.SetPrg16((uint)0xC000, (uint)(bank + 1));
            Mapping.SetChr8(0);
        }
    }

    /// <summary>
    /// Mapper 9 / MMC2 (Punch-Out!!).
    /// $8000-$9FFF is switchable; the upper 24 KB is fixed to the last three
    /// 8 KB banks. CHR is selected by four latches, normally triggered by
    /// pattern-table fetches from tiles $FD/$FE.
    /// </summary>
    public sealed class Mmc2Board : BoardBase
    {
        private byte _prgBank;
        private readonly byte[] _latch = new byte[4];

        public Mmc2Board(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 9;

        public override void Power()
        {
            base.Power();
            _prgBank = 0;
            for (int i = 0; i < _latch.Length; i++)
                _latch[i] = 0;
            Sync();
        }

        public override void Reset()
        {
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0xA000)
                return;

            switch (address & 0xF000)
            {
                case 0xA000:
                    _prgBank = value;
                    Mapping.SetPrg8((uint)0x8000, _prgBank);
                    break;

                case 0xB000:
                    _latch[0] = value;
                    Mapping.SetChr4(0x0000, _latch[0]);
                    break;

                case 0xC000:
                    _latch[1] = value;
                    Mapping.SetChr4(0x0000, _latch[1]);
                    break;

                case 0xD000:
                    _latch[2] = value;
                    Mapping.SetChr4(0x1000, _latch[2]);
                    break;

                case 0xE000:
                    _latch[3] = value;
                    Mapping.SetChr4(0x1000, _latch[3]);
                    break;

                case 0xF000:
                    if ((Cart.Flags6 & 0x08) == 0)
                    {
                        Cart.Mirroring = (value & 1) != 0
                            ? (int)MirroringMode.Horizontal
                            : (int)MirroringMode.Vertical;
                    }

                    break;
            }
        }

        private void Sync()
        {
            int last = LastPrgBank8();

            Mapping.SetPrg8((uint)0x8000, _prgBank);
            Mapping.SetPrg8((uint)0xA000, (uint)(last - 2));
            Mapping.SetPrg8((uint)0xC000, (uint)(last - 1));
            Mapping.SetPrg8((uint)0xE000, (uint)last);

            Mapping.SetChr4(0x0000, _latch[0]);
            Mapping.SetChr4(0x1000, _latch[2]);
        }
    }
}
