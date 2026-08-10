namespace NesLifter.Core;

public static class NesRomExtensions
{
    public static int AddrToOffset(this NesRom rom, ushort addr)
    {
        if (addr < 0x8000) return -1;

        int len = rom.PrgRom.Length;
        if (len == 0) return -1;

        // 16 KB PRG: mirror $8000-$BFFF into $C000-$FFFF
        if (len == 0x4000)
        {
            return addr >= 0xC000
                ? addr - 0xC000
                : addr - 0x8000;
        }

        // 32 KB PRG: linear $8000-$FFFF
        if (len == 0x8000)
        {
            return addr - 0x8000;
        }

        // Larger PRG: keep last 32 KB fixed for now.
        if (len > 0x8000)
        {
            int baseIndex = len - 0x8000;
            int offset = baseIndex + (addr - 0x8000);

            return offset >= 0 && offset < len ? offset : -1;
        }

        // Fallback: modular mirroring.
        int a = addr >= 0xC000 ? addr - 0xC000 : addr - 0x8000;
        if (a < 0) a = addr - 0x8000;

        return a % len;
    }
}