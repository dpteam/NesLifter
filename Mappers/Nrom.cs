using NesLifter.Core;
using NesLifter.Core.Mappers;

namespace NesLifter.Mappers
{
    public sealed class Nrom : IMapper
    {
        private readonly NesRom _rom;

        public Nrom(NesRom rom)
        {
            _rom = rom;
        }

        public int Id
        {
            get { return 0; }
        }

        public void Reset()
        {
        }

        public byte ReadPrg(ushort address)
        {
            byte[] prg = _rom.PrgRom;
            int len = prg.Length;

            if (len == 0)
                return 0;

            int offset = address - 0x8000;

            if (len == 0x4000)
                offset &= 0x3FFF;

            if (offset < 0)
                offset = 0;

            return prg[offset % len];
        }

        public void WritePrg(ushort address, byte value)
        {
            // NROM PRG is read-only.
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
            // CHR ROM read-only; CHR RAM later.
        }
    }
}