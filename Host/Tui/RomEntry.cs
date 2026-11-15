using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NesLifter.Core;

namespace NesLifter.Host.Tui;

/// <summary>
/// One cartridge as the TUI sees it: enough to show a useful row without
/// lifting anything.
/// </summary>
public sealed class RomEntry
{
    public RomEntry(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }

    /// <summary>Mapper number, or -1 when the header could not be read.</summary>
    public int Mapper { get; private set; } = -1;

    /// <summary>Board name from the catalog, or a diagnostic string.</summary>
    public string MapperName { get; private set; } = "?";

    public bool MapperSupported { get; private set; }

    public long PrgSize { get; private set; }
    public long ChrSize { get; private set; }
    public uint Crc32 { get; private set; }

    /// <summary>Set when the header could not be parsed.</summary>
    public string Error { get; private set; } = string.Empty;

    /// <summary>True once the entry has been lifted in this session.</summary>
    public bool Lifted { get; set; }

    /// <summary>Result summary of the last lift, e.g. "ok" or "trap".</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Reads the header. Cheap: no PRG/CHR is kept.</summary>
    public void Probe()
    {
        try
        {
            CartInfo cart = InesLoader.Load(Path);

            Mapper = cart.Mapper;
            MapperName = Mappers.MapperCatalog.Get(cart.Mapper).Name;
            MapperSupported = Core.Boards.BoardFactory.IsSupported(cart.Mapper);
            PrgSize = cart.PrgRomSize;
            ChrSize = cart.ChrRomSize;
            Crc32 = cart.Crc32;
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            Mapper = -1;
            MapperName = "unreadable";
            MapperSupported = false;
            Error = ex.Message;
        }
    }

    public string SizeLabel()
    {
        long kb = (PrgSize + ChrSize) / 1024;
        return kb >= 1024
            ? (kb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "M"
            : kb + "K";
    }
}

/// <summary>
/// Discovers cartridges on disk and probes their headers in parallel.
/// </summary>
public static class RomScanner
{
    public static List<RomEntry> Scan(string root, bool recursive)
    {
        List<string> files = new List<string>();

        try
        {
            if (File.Exists(root))
            {
                files.Add(Path.GetFullPath(root));
            }
            else if (Directory.Exists(root))
            {
                SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                files.AddRange(Directory.GetFiles(root, "*.nes", option));
            }
        }
        catch (Exception)
        {
            // An unreadable folder yields an empty list, not a crash.
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);

        List<RomEntry> entries = new List<RomEntry>(files.Count);

        foreach (string file in files)
            entries.Add(new RomEntry(file));

        return entries;
    }

    /// <summary>Probes headers with a progress callback so the TUI can animate.</summary>
    public static void ProbeAll(IReadOnlyList<RomEntry> entries, Action<int, int>? progress = null)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Probe();
            progress?.Invoke(i + 1, entries.Count);
        }
    }
}
