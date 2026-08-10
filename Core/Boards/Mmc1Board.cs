namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 1 / MMC1. Based on FCEUmm's asic_mmc1.c and mmc1.c.
    /// </summary>
    public sealed class Mmc1Board : BoardBase
    {
        private readonly byte[] _registers = new byte[4];

        private byte _shift;
        private byte _bits;
        private int _writeFilter;

        public Mmc1Board(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId
        {
            get { return 1; }
        }

        public override void Power()
        {
            base.Power();
            ClearRegisters();
        }

        public override void Reset()
        {
            // A console reset does not clear the MMC1's serial registers.
            Sync();
        }

        public override void ClockCpu(int cycles)
        {
            if (cycles <= 0 || _writeFilter <= 0)
                return;

            _writeFilter -= cycles;
            if (_writeFilter < 0)
                _writeFilter = 0;
        }

        public override byte ReadLow(ushort address)
        {
            if (address < 0x6000 || address >= 0x8000)
                return 0;

            // PRG RAM disabled: FCEUmm returns the high address byte.
            if ((_registers[3] & 0x10) != 0)
                return (byte)(address >> 8);

            return Mapping.ReadPrg(address);
        }

        public override void WriteLow(ushort address, byte value)
        {
            if (address >= 0x6000 && address < 0x8000 &&
                (_registers[3] & 0x10) == 0)
            {
                Mapping.WritePrg(address, value);
            }
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            // Reset writes are accepted even during the RMW write filter.
            if ((value & 0x80) != 0)
            {
                _registers[0] |= 0x0C;
                _shift = 0;
                _bits = 0;
                _writeFilter = 2;
                Sync();
                return;
            }

            if (_writeFilter != 0)
            {
                _writeFilter = 2;
                return;
            }

            _shift |= (byte)((value & 1) << _bits);
            _bits++;

            if (_bits == 5)
            {
                _registers[(address >> 13) & 3] = _shift;
                _shift = 0;
                _bits = 0;
                Sync();
            }

            _writeFilter = 2;
        }

        private void ClearRegisters()
        {
            _registers[0] = 0x0C;
            _registers[1] = 0;
            _registers[2] = 0;
            _registers[3] = 0;
            _shift = 0;
            _bits = 0;
            _writeFilter = 0;
            Sync();
        }

        private void Sync()
        {
            SyncMirroring();
            SyncPrg();
            SyncChr();
        }

        private void SyncMirroring()
        {
            if ((Cart.Flags6 & 0x08) != 0)
                return;

            Cart.Mirroring = (_registers[0] & 3) switch
            {
                0 => (int)MirroringMode.OneScreenLow,
                1 => (int)MirroringMode.OneScreenHigh,
                2 => (int)MirroringMode.Vertical,
                _ => (int)MirroringMode.Horizontal
            };
        }

        private void SyncPrg()
        {
            int outerBank = _registers[1] & 0x10;
            int prgBank = _registers[3] & 0x0F;

            switch ((_registers[0] >> 2) & 3)
            {
                case 0:
                case 1:
                    int firstBank = (prgBank & 0x0E) + outerBank;
                    Mapping.SetPrg16(0x8000, (uint)firstBank);
                    Mapping.SetPrg16(0xC000, (uint)(firstBank + 1));
                    break;

                case 2:
                    Mapping.SetPrg16(0x8000, (uint)outerBank);
                    Mapping.SetPrg16(0xC000, (uint)(prgBank + outerBank));
                    break;

                default:
                    Mapping.SetPrg16(0x8000, (uint)(prgBank + outerBank));
                    Mapping.SetPrg16(0xC000, (uint)(0x0F + outerBank));
                    break;
            }
        }

        private void SyncChr()
        {
            if ((_registers[0] & 0x10) == 0)
            {
                // MMC1 stores this bank number in 4 KiB units.
                Mapping.SetChr8((uint)(_registers[1] >> 1));
                return;
            }

            Mapping.SetChr4(0x0000, _registers[1]);
            Mapping.SetChr4(0x1000, _registers[2]);
        }
    }
}
