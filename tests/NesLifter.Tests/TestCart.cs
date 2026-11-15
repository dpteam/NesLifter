using NesLifter.Core;
using NesLifter.Core.Boards;
using Xunit;

namespace NesLifter.Tests;

/// <summary>
/// Shared helpers for building cartridges in memory.
/// </summary>
internal static class TestCart
{
    /// <summary>
    /// Builds a cartridge with a PRG ROM whose bytes are
    /// <c>prg[offset] = (offset &gt;&gt; 8) ^ offset</c>, so every byte position
    /// is identifiable. Bank <c>n</c> starts with <c>n</c> in its first byte.
    /// </summary>
    public static CartInfo Create(int mapper, int prgSize, int chrSize = 0, int mirroring = 1)
    {
        byte[] prg = new byte[prgSize];

        for (int bank = 0; bank < prgSize / 0x4000; bank++)
        {
            for (int i = 0; i < 0x4000; i++)
            {
                int offset = bank * 0x4000 + i;
                prg[offset] = i == 0 ? (byte)bank : (byte)(offset ^ (offset >> 8));
            }
        }

        byte[] chr = new byte[chrSize];

        for (int bank = 0; bank < chrSize / 0x2000; bank++)
        {
            for (int i = 0; i < 0x2000; i++)
            {
                int offset = bank * 0x2000 + i;
                chr[offset] = i == 0 ? (byte)bank : (byte)(offset & 0xFF);
            }
        }

        return new CartInfo
        {
            Mapper = mapper,
            Mirroring = mirroring,
            PrgRom = prg,
            ChrRom = chr,
            PrgRomSize = prgSize,
            ChrRomSize = chrSize,
            Flags6 = (byte)(mirroring & 1),
            Flags7 = (byte)(mapper & 0xF0)
        };
    }

    /// <summary>Creates a board and powers it on.</summary>
    public static (CartInfo Cart, CartMapping Mapping, IBoard Board) CreatePowered(
        int mapper,
        int prgSize,
        int chrSize = 0,
        int mirroring = 1)
    {
        CartInfo cart = Create(mapper, prgSize, chrSize, mirroring);
        CartMapping mapping = new CartMapping();
        IBoard board = BoardFactory.Create(cart, mapping);
        board.Power();
        return (cart, mapping, board);
    }

    /// <summary>Reads the PRG byte the CPU would see at <paramref name="address"/>.</summary>
    public static byte CpuRead(CartMapping mapping, ushort address)
    {
        int page = (address >> 11) & 31;
        byte[] mem = mapping.GetPrgPageMemory(page);

        if (mem == null)
            return 0;

        int offset = mapping.GetPrgPageAdjust(page) + address;
        return (uint)offset < (uint)mem.Length ? mem[offset] : (byte)0;
    }

    /// <summary>Expected byte at a PRG ROM offset.</summary>
    public static byte PrgByte(CartInfo cart, int offset)
    {
        return cart.PrgRom[offset];
    }

    /// <summary>Expected byte at the start of a 16 KB PRG bank.</summary>
    public static byte PrgBankMarker(CartInfo cart, int bank16)
    {
        return cart.PrgRom[bank16 * 0x4000];
    }

    /// <summary>Expected byte at the start of an 8 KB PRG bank.</summary>
    public static byte PrgBankMarker8(CartInfo cart, int bank8)
    {
        return cart.PrgRom[bank8 * 0x2000];
    }
}
