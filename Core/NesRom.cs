using System;

namespace NesLifter.Core;

/// <summary>
/// Compatibility wrapper over new CartInfo.
/// Старый код может продолжать работать с NesRom,
/// а новая архитектура постепенно переходит на CartInfo.
/// </summary>
public sealed class NesRom
{
    private readonly CartInfo _cart;

    public CartInfo Cart => _cart;

    public byte[] PrgRom => _cart.PrgRom;
    public byte[] ChrRom => _cart.ChrRom;

    public int Mapper => _cart.Mapper;

    public string Mirroring => MirroringToString(_cart.Mirroring);

    public bool HasBattery => _cart.Battery;
    public bool HasTrainer => _cart.TrainerPresent;

    public byte Flags6 => _cart.Flags6;
    public byte Flags7 => _cart.Flags7;

    private NesRom(CartInfo cart)
    {
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));
    }

    public static NesRom Load(string path)
    {
        CartInfo cart = InesLoader.Load(path);
        return new NesRom(cart);
    }

    public int AddrToOffset(ushort addr)
    {
        if (addr < 0x8000)
            return -1;

        int len = PrgRom.Length;
        if (len == 0)
            return -1;

        // 16 KB PRG: mirror $8000-$BFFF into $C000-$FFFF
        if (len == 0x4000)
        {
            if (addr >= 0xC000)
                return addr - 0xC000;

            return addr - 0x8000;
        }

        // 32 KB PRG: linear $8000-$FFFF
        if (len == 0x8000)
        {
            return addr - 0x8000;
        }

        // Large PRG: currently assume last 32 KB fixed.
        if (len > 0x8000)
        {
            int baseIndex = len - 0x8000;
            int offset = baseIndex + (addr - 0x8000);

            if (offset >= 0 && offset < len)
                return offset;

            return -1;
        }

        // Non-standard size: modular mirroring.
        int a = addr >= 0xC000 ? addr - 0xC000 : addr - 0x8000;

        if (a < 0)
            a = addr - 0x8000;

        return a % len;
    }

    public ushort ReadVector(ushort vectorAddr)
    {
        byte[] prg = PrgRom;
        int len = prg.Length;

        if (len < 6)
            return 0;

        int off;

        if (vectorAddr == 0xFFFA)
        {
            off = len - 6;
        }
        else if (vectorAddr == 0xFFFC)
        {
            off = len - 4;
        }
        else if (vectorAddr == 0xFFFE)
        {
            off = len - 2;
        }
        else
        {
            off = AddrToOffset(vectorAddr);

            if (off < 0 || off + 1 >= len)
                return 0;
        }

        return (ushort)(prg[off] | (prg[off + 1] << 8));
    }

    private static string MirroringToString(int mirroring)
    {
        return mirroring switch
        {
            1 => "Vertical",
            2 => "Four-screen",
            _ => "Horizontal"
        };
    }
}