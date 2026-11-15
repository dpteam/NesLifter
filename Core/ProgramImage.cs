using System;
using NesLifter.Core.Boards;

namespace NesLifter.Core;

/// <summary>
/// Read-only view of the CPU address space as it looks immediately after
/// power-on, with the cartridge mapper applied.
///
/// The disassembler must see the same bytes the 6502 would see, otherwise
/// banked cartridges are analysed against the wrong PRG layout. For mappers
/// that are not implemented yet the image falls back to the flat
/// "last 32 KB is linear" model that older versions of NesLifter used.
/// </summary>
public sealed class ProgramImage
{
    private readonly NesRom _rom;
    private readonly CartMapping? _mapping;
    private readonly bool _mapperAware;

    private ProgramImage(NesRom rom, CartMapping? mapping, bool mapperAware)
    {
        _rom = rom;
        _mapping = mapping;
        _mapperAware = mapperAware;
    }

    /// <summary>True when a board supplied a real power-on bank layout.</summary>
    public bool MapperAware => _mapperAware;

    /// <summary>PRG ROM bytes, for size checks and hashing.</summary>
    public byte[] PrgRom => _rom.PrgRom;

    /// <summary>
    /// Builds the image. <paramref name="mapping"/> must already be powered on.
    /// </summary>
    public static ProgramImage Create(NesRom rom, CartMapping? mapping)
    {
        if (rom == null)
            throw new ArgumentNullException(nameof(rom));

        return new ProgramImage(rom, mapping, mapping != null);
    }

    /// <summary>
    /// Builds the image directly from a cartridge, powering a board if one
    /// exists. Never throws: an unsupported mapper yields a flat image.
    /// </summary>
    public static ProgramImage Create(NesRom rom, out string layout, out bool fallback)
    {
        CartMapping mapping = new CartMapping();

        try
        {
            IBoard board = BoardFactory.CreateOrDefault(rom.Cart, mapping, out fallback);
            board.Power();
            layout = "mapper " + rom.Cart.Mapper;
            return new ProgramImage(rom, mapping, true);
        }
        catch (Exception)
        {
            fallback = true;
            layout = "flat";
            return new ProgramImage(rom, null, false);
        }
    }

    /// <summary>
    /// Reads a byte from the CPU address space. Only cartridge space
    /// ($4020-$FFFF) is meaningful for static analysis; RAM and I/O return 0.
    /// </summary>
    public byte Read(ushort address)
    {
        if (address < 0x4020)
            return 0;

        int offset = ToOffset(address);

        if (offset < 0)
            return 0;

        byte[] prg = _rom.PrgRom;
        return (uint)offset < (uint)prg.Length ? prg[offset] : (byte)0;
    }

    /// <summary>
    /// Maps a CPU address to a PRG ROM offset, or -1 when the address is not
    /// backed by PRG ROM (WRAM, open bus, unmapped bank).
    /// </summary>
    public int ToOffset(ushort address)
    {
        if (address < 0x4020)
            return -1;

        if (_mapperAware && _mapping != null)
        {
            int page = (address >> 11) & 31;
            byte[]? mem = _mapping.GetPrgPageMemory(page);

            if (mem == null || !ReferenceEquals(mem, _rom.PrgRom))
                return -1;

            int offset = _mapping.GetPrgPageAdjust(page) + address;
            return (uint)offset < (uint)_rom.PrgRom.Length ? offset : -1;
        }

        return FlatOffset(address);
    }

    /// <summary>True when the address decodes to real PRG ROM.</summary>
    public bool IsCodeSpace(ushort address)
    {
        return address >= 0x8000 && address < 0xFFFA && ToOffset(address) >= 0;
    }

    /// <summary>
    /// Legacy flat mapping: 16 KB mirroring, 32 KB linear, or "last 32 KB"
    /// for larger ROMs. Kept as the fallback for unknown mappers so that
    /// previously working dumps keep working.
    /// </summary>
    private int FlatOffset(ushort address)
    {
        if (address < 0x8000)
            return -1;

        byte[] prg = _rom.PrgRom;
        int len = prg.Length;

        if (len == 0)
            return -1;

        if (len == 0x4000)
            return address >= 0xC000 ? address - 0xC000 : address - 0x8000;

        if (len == 0x8000)
            return address - 0x8000;

        if (len > 0x8000)
        {
            int offset = len - 0x8000 + (address - 0x8000);
            return (uint)offset < (uint)len ? offset : -1;
        }

        int a = address >= 0xC000 ? address - 0xC000 : address - 0x8000;

        if (a < 0)
            a = address - 0x8000;

        return a % len;
    }
}
