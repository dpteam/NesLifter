using NesLifter.Core;
using NesLifter.Core.Mappers;

namespace NesLifter.Mappers
{
    public sealed class CNROM : IMapper
    {
        private readonly NesRom _rom;
        private int _chrBank;

        public CNROM(NesRom rom)
        {
            _rom = rom;
        }

        public int Id
        {
            get { return 3; }
        }

        public void Reset()
        {
            _chrBank = 0;
        }

        public byte ReadPrg(ushort address)
        {
            byte[] prg = _rom.PrgRom;
            int len = prg.Length;

            if (len == 0)
                return 0;

            int offset = address - 0x8000;

            if (len == 0x4000 && address >= 0xC000)
                offset -= 0x4000;

            if (offset < 0)
                offset = 0;

            return prg[offset % len];
        }

        public void WritePrg(ushort address, byte value)
        {
            int banks = _rom.ChrRom.Length / 0x2000;

            if (banks <= 0)
                return;

            _chrBank = SelectBank(value, banks);
        }

        public byte ReadChr(ushort address)
        {
            byte[] chr = _rom.ChrRom;
            int len = chr.Length;

            if (len == 0)
                return 0;

            int offset = (_chrBank * 0x2000) + (address & 0x1FFF);

            if (offset >= len)
                offset %= len;

            return chr[offset];
        }

        public void WriteChr(ushort address, byte value)
        {
            // CNROM bank switching is normally done via PRG writes.
        }

        private static int SelectBank(int value, int banks)
        {
            if (banks <= 1)
                return 0;

            // Если количество банков кратно степени двойки — можно маской.
            if ((banks & (banks - 1)) == 0)
                return value & (banks - 1);

            return value % banks;
        }
    }
}