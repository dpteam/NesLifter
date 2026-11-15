using System;
using System.Collections.Generic;

namespace NesLifter.Mappers;

/// <summary>
/// Banking behaviour family. One family maps to one implementation on the
/// analysis side (<see cref="NesLifter.Core.Boards"/>) and one emitter on the
/// code-generation side (<see cref="NesLifter.Lifting.MapperRuntimeEmitter"/>).
/// </summary>
public enum MapperFamily
{
    Nrom,
    Mmc1,
    UxRom,
    Cnrom,
    Mmc3,
    AxRom,
    ColorDreams,
    GxRom,
    Camerica,
    Nina,
    Mmc2,
    Fme7,
    Vrc2,
    Vrc4,
    Vrc6,
    Quattro,
    Bnrom,
    Cprom
}

/// <summary>
/// Immutable description of a supported mapper.
/// </summary>
public sealed class MapperDescriptor
{
    public MapperDescriptor(
        int id,
        string name,
        MapperFamily family,
        string summary,
        int defaultPrgRam = 0x2000,
        bool battery = false,
        bool busConflict = false,
        bool hasIrq = false)
    {
        Id = id;
        Name = name;
        Family = family;
        Summary = summary;
        DefaultPrgRam = defaultPrgRam;
        Battery = battery;
        BusConflict = busConflict;
        HasIrq = hasIrq;
    }

    /// <summary>iNES mapper number.</summary>
    public int Id { get; }

    /// <summary>Human readable board name, for reports.</summary>
    public string Name { get; }

    /// <summary>Banking behaviour family.</summary>
    public MapperFamily Family { get; }

    /// <summary>One-line description used in the analysis report.</summary>
    public string Summary { get; }

    /// <summary>Default PRG RAM size in bytes for iNES 1.0 headers.</summary>
    public int DefaultPrgRam { get; }

    /// <summary>Board ships battery-backed save RAM.</summary>
    public bool Battery { get; }

    /// <summary>Writes are ANDed with the value currently on the bus.</summary>
    public bool BusConflict { get; }

    /// <summary>Board can raise a scanline IRQ.</summary>
    public bool HasIrq { get; }

    public override string ToString()
    {
        return Id + " (" + Name + ")";
    }
}

/// <summary>
/// Registry of every mapper NesLifter can analyse and re-emit.
/// Mapper numbers that are not listed here fall back to a read-only
/// "fixed 32 KB at $8000" model and are reported as unsupported.
/// </summary>
public static class MapperCatalog
{
    private static readonly Dictionary<int, MapperDescriptor> ById = Build();

    public static IReadOnlyCollection<MapperDescriptor> All => ById.Values;

    public static bool TryGet(int mapperId, out MapperDescriptor descriptor)
    {
        return ById.TryGetValue(mapperId, out descriptor!);
    }

    public static MapperDescriptor Get(int mapperId)
    {
        return ById.TryGetValue(mapperId, out MapperDescriptor? descriptor)
            ? descriptor
            : new MapperDescriptor(mapperId, "Unsupported", MapperFamily.Nrom,
                "No dedicated implementation; treated as fixed 32 KB PRG.");
    }

    public static bool IsSupported(int mapperId)
    {
        return ById.ContainsKey(mapperId);
    }

    private static Dictionary<int, MapperDescriptor> Build()
    {
        var list = new[]
        {
            new MapperDescriptor(0, "NROM", MapperFamily.Nrom,
                "Fixed 16/32 KB PRG, fixed 8 KB CHR ROM or CHR RAM."),

            new MapperDescriptor(1, "MMC1", MapperFamily.Mmc1,
                "Serial shift register; 16 KB PRG modes, 4/8 KB CHR, 4 mirroring modes.",
                battery: true),

            new MapperDescriptor(2, "UxROM", MapperFamily.UxRom,
                "16 KB switchable PRG at $8000, last bank fixed at $C000. CHR RAM.",
                defaultPrgRam: 0x2000),

            new MapperDescriptor(3, "CNROM", MapperFamily.Cnrom,
                "Fixed PRG, 8 KB switchable CHR. Discrete logic with bus conflicts."),

            new MapperDescriptor(4, "MMC3", MapperFamily.Mmc3,
                "8 KB PRG / 1-2 KB CHR banking, scanline IRQ, 4 mirroring modes.",
                battery: true, hasIrq: true),

            new MapperDescriptor(7, "AxROM", MapperFamily.AxRom,
                "32 KB switchable PRG, single-screen mirroring selected by the bank byte."),

            new MapperDescriptor(9, "MMC2", MapperFamily.Mmc2,
                "Latch-based 4 KB CHR switching triggered by pattern fetches (Punch-Out!!)."),

            new MapperDescriptor(11, "Color Dreams", MapperFamily.ColorDreams,
                "32 KB PRG and 8 KB CHR from one register, bus conflicts."),

            new MapperDescriptor(13, "CPROM", MapperFamily.Cprom,
                "Fixed 32 KB PRG, 16 KB CHR RAM with 4 KB banking."),

            new MapperDescriptor(21, "VRC4a/c", MapperFamily.Vrc4,
                "Konami VRC4: 8 KB PRG, 1 KB CHR, scanline IRQ.", hasIrq: true),

            new MapperDescriptor(22, "VRC2a", MapperFamily.Vrc2,
                "Konami VRC2: 16 KB PRG, 2 KB CHR, no IRQ."),

            new MapperDescriptor(23, "VRC4e/f", MapperFamily.Vrc4,
                "Konami VRC4 with alternate register lines.", hasIrq: true),

            new MapperDescriptor(24, "VRC6a", MapperFamily.Vrc6,
                "Konami VRC6: 8 KB PRG, 1 KB CHR, scanline IRQ, 3 expansion sound channels.",
                hasIrq: true),

            new MapperDescriptor(25, "VRC4b/d", MapperFamily.Vrc4,
                "Konami VRC4 with alternate register lines.", hasIrq: true),

            new MapperDescriptor(26, "VRC6b", MapperFamily.Vrc6,
                "Konami VRC6 with swapped PRG/CHR register addresses.", hasIrq: true),

            new MapperDescriptor(34, "BNROM / NINA-001", MapperFamily.Bnrom,
                "32 KB switchable PRG; NINA-001 variant adds 4 KB CHR banking."),

            new MapperDescriptor(66, "GxROM", MapperFamily.GxRom,
                "32 KB PRG and 8 KB CHR from a single register, bus conflicts."),

            new MapperDescriptor(69, "Sunsoft FME-7", MapperFamily.Fme7,
                "Command register banking, 1 KB CHR, scanline IRQ, 8 KB WRAM.", hasIrq: true),

            new MapperDescriptor(71, "Camerica / Codemasters", MapperFamily.Camerica,
                "UxROM variant with a mirroring bit and bus conflicts."),

            new MapperDescriptor(79, "NINA-03 / NINA-06", MapperFamily.Nina,
                "Fixed 32 KB PRG, 8 KB CHR banking, mirroring bit, bus conflicts."),

            new MapperDescriptor(87, "Jaleco CHR", MapperFamily.Nina,
                "Fixed PRG, 8 KB CHR banking, no mirroring bit."),

            new MapperDescriptor(88, "Namco 118", MapperFamily.Nina,
                "Fixed PRG with CHR banking and mirroring, like DxROM."),

            new MapperDescriptor(206, "Namco 118 / DxROM", MapperFamily.Mmc3,
                "MMC3 clone without IRQ; used by many Tengen and Namco titles.",
                battery: true),

            new MapperDescriptor(232, "Codemasters Quattro", MapperFamily.Quattro,
                "Four 128 KB blocks selected by address bits; 64 KB outer bank register.")
        };

        var map = new Dictionary<int, MapperDescriptor>(list.Length);

        foreach (MapperDescriptor descriptor in list)
            map[descriptor.Id] = descriptor;

        return map;
    }
}
