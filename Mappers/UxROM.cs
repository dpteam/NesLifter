using NesLifter.Core;
using NesLifter.Core.Mappers;

namespace NesLifter.Mappers
{
    public sealed class UxROM : IMapper
    {
        private readonly NesRom _rom;
        private int _prgBank;

        public UxROM(NesRom rom)
        {
            _rom = rom;
        }

        public int Id
        {
            get { return 2; }
        }

        public void Reset()
        {
            _prgBank = 0;
        }

        public byte ReadPrg(ushort address)
        {
            byte[] prg = _rom.PrgRom;
            int len = prg.Length;

            if (len == 0)
                return 0;

            if (address < 0xC000)
            {
                int offset = (_prgBank * 0x4000) + (address - 0x8000);

                if (offset < 0)
                    offset = 0;

                return prg[offset % len];
            }
            else
            {
                int lastBankOffset = len - 0x4000;

                if (lastBankOffset < 0)
                    lastBankOffset = 0;

                int offset = lastBankOffset + (address - 0xC000);

                if (offset < 0)
                    offset = 0;

                return prg[offset % len];
            }
        }

        public void WritePrg(ushort address, byte value)
        {
            _prgBank = value & 0x0F;
        }

        public byte ReadChr(ushort address)
        {
            byte[] chr = _rom.ChrRom;
            int len = chr.Length;

            if (len == 0)
                return 0;

            int offset = address & 0x1FFF;

            return chr[offset % len];
        }

        public void WriteChr(ushort address, byte value)
        {
            // CHR RAM write placeholder.
        }
    }
}