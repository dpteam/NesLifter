using NesLifter.Core;
using NesLifter.Core.Mappers;

namespace NesLifter.Mappers
{
    public sealed class MMC1 : IMapper
    {
        private readonly NesRom _rom;

        private int _shiftReg;
        private int _writeCount;

        private int _control;
        private int _chrBank0;
        private int _chrBank1;
        private int _prgBank;

        public MMC1(NesRom rom)
        {
            _rom = rom;
        }

        public int Id
        {
            get { return 1; }
        }

        public void Reset()
        {
            _shiftReg = 0x10;
            _writeCount = 0;

            _control = 0x0C;
            _chrBank0 = 0;
            _chrBank1 = 0;
            _prgBank = 0;
        }

        public byte ReadPrg(ushort address)
        {
            byte[] prg = _rom.PrgRom;
            int len = prg.Length;

            if (len == 0)
                return 0;

            // Пока временный линейный стаб.
            // Полная банковая логика MMC1 будет позже вместе с IBoard/CartMapping.
            int offset = address - 0x8000;

            if (offset < 0)
                offset = 0;

            return prg[offset % len];
        }

        public void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            if ((value & 0x80) != 0)
            {
                Reset();
                return;
            }

            _shiftReg = (_shiftReg >> 1) | ((value & 1) << 4);
            _writeCount++;

            if (_writeCount != 5)
                return;

            int loadedValue = _shiftReg & 0x1F;

            int register;

            if (address < 0xA000)
                register = 0;
            else if (address < 0xC000)
                register = 1;
            else if (address < 0xE000)
                register = 2;
            else
                register = 3;

            switch (register)
            {
                case 0:
                    _control = loadedValue;
                    break;

                case 1:
                    _chrBank0 = loadedValue;
                    break;

                case 2:
                    _chrBank1 = loadedValue;
                    break;

                case 3:
                    _prgBank = loadedValue;
                    break;
            }

            _shiftReg = 0x10;
            _writeCount = 0;
        }

        public byte ReadChr(ushort address)
        {
            byte[] chr = _rom.ChrRom;
            int len = chr.Length;

            if (len == 0)
                return 0;

            int bank = address < 0x1000 ? _chrBank0 : _chrBank1;
            int offset = (bank * 0x1000) + (address & 0x0FFF);

            if (offset >= len)
                offset %= len;

            return chr[offset];
        }

        public void WriteChr(ushort address, byte value)
        {
            // CHR ROM read-only; CHR RAM later.
        }
    }
}