using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NesLifter.Common;
using NesLifter.Compilation;
using NesLifter.Core;
using NesLifter.Core.Boards;
using NesLifter.Lifting;
using NesLifter.Mappers;

namespace NesLifter.Host;

/// <summary>
/// Orchestrates one lift: read cartridge, build the power-on image, analyse,
/// generate C#, compile, and write reports.
/// </summary>
public sealed class Pipeline
{
    private readonly CliOptions _options;
    private readonly RoslynCompiler _compiler = new RoslynCompiler();

    public Pipeline(CliOptions options)
    {
        _options = options;
    }

    public int Run()
    {
        try
        {
            Directory.CreateDirectory(_options.OutputPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to create output directory: " + ex.Message);
            return 1;
        }

        List<string> files = CollectFiles();

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No input .nes files found.");
            return 1;
        }

        Console.WriteLine("Files found: " + files.Count);
        Console.WriteLine("Output directory: " + _options.OutputPath);
        Console.WriteLine();

        int ok = 0;
        int failed = 0;

        foreach (string file in files)
        {
            Console.WriteLine("=== Processing: " + Path.GetFileName(file) + " ===");

            try
            {
                if (ProcessFile(file))
                    ok++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine("ERROR: " + ex.Message);

                if (_options.Verbose)
                    Console.Error.WriteLine(ex.ToString());
            }

            Console.WriteLine();
        }

        Console.WriteLine("Summary: ok=" + ok + ", failed=" + failed);
        return failed == 0 ? 0 : 2;
    }

    private List<string> CollectFiles()
    {
        List<string> files = new List<string>();

        if (string.IsNullOrWhiteSpace(_options.InputPath))
            return files;

        try
        {
            if (File.Exists(_options.InputPath))
            {
                files.Add(Path.GetFullPath(_options.InputPath));
            }
            else if (Directory.Exists(_options.InputPath))
            {
                SearchOption search = _options.Recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                files.AddRange(Directory.GetFiles(_options.InputPath, "*.nes", search));
            }
            else
            {
                Console.Error.WriteLine("Input path not found: " + _options.InputPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("File collection error: " + ex.Message);
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private bool ProcessFile(string file)
    {
        string romName = Path.GetFileNameWithoutExtension(file);
        string safeName = FileSystemUtil.SanitizeFileName(romName);

        string workDir = Path.Combine(_options.OutputPath, safeName);
        string srcDir = Path.Combine(workDir, "src");

        Directory.CreateDirectory(workDir);

        if (_options.SaveSource)
            Directory.CreateDirectory(srcDir);

        NesRom rom = NesRom.Load(file);
        CartInfo cart = rom.Cart;
        MapperDescriptor descriptor = MapperCatalog.Get(cart.Mapper);
        bool supported = BoardFactory.IsSupported(cart.Mapper);

        Console.WriteLine(
            "iNES: PRG=" + cart.PrgRomSize +
            ", CHR=" + cart.ChrRomSize +
            ", Mapper=" + cart.Mapper +
            ", Submapper=" + cart.Submapper +
            ", iNES2=" + cart.INes2 +
            ", Mirroring=" + cart.Mirroring +
            ", Battery=" + cart.Battery +
            ", Region=" + cart.Region);

        Console.WriteLine(
            "RAM: PRG-RAM=" + cart.PrgRamSize + "+" + cart.PrgRamSaveSize +
            ", CHR-RAM=" + cart.ChrRamSize + "+" + cart.ChrRamSaveSize);

        Console.WriteLine(
            "Vectors: NMI=0x" + rom.ReadVector(0xFFFA).ToString("X4") +
            ", RESET=0x" + rom.ReadVector(0xFFFC).ToString("X4") +
            ", IRQ=0x" + rom.ReadVector(0xFFFE).ToString("X4"));

        Console.WriteLine(
            "CRC32: PRG=0x" + cart.PrgCrc32.ToString("X8") +
            ", CHR=0x" + cart.ChrCrc32.ToString("X8") +
            ", Combined=0x" + cart.Crc32.ToString("X8"));

        Console.WriteLine("Board: " + descriptor.Id + " " + descriptor.Name + " - " + descriptor.Summary);

        if (!supported)
        {
            Console.WriteLine("Board: mapper is not implemented; falling back to a flat PRG layout.");
        }

        // === Power-on image ===
        CartMapping mapping = new CartMapping();

        try
        {
            IBoard board = BoardFactory.CreateOrDefault(cart, mapping, out bool fallback);
            board.Power();

            Console.WriteLine(fallback
                ? "Board: flat fallback in use."
                : "Board: power-on bank layout established.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Board: initialization failed (" + ex.Message + "); using a flat layout.");
            mapping = new CartMapping();
        }

        ProgramImage image = ProgramImage.Create(rom, mapping);

        Console.WriteLine(
            "Analysis image: " + (image.MapperAware ? "mapper-aware" : "flat") +
            " (" + image.PrgRom.Length + " bytes PRG)");

        // === Pass 1 ===
        EnsureDynamicTargetsFile(workDir);

        Console.WriteLine("--- Pass 1: initial analysis ---");

        if (!RunPass(rom, image, descriptor, supported, workDir, srcDir, safeName))
            return false;

        if (_options.NoCompile)
            return true;

        // === Pass 2: re-lift when the previous run discovered new targets ===
        string dynLog = Path.Combine(workDir, "dynamic_targets.log");

        if (File.Exists(dynLog))
        {
            List<ushort> discovered = ReadDynamicTargets(dynLog);

            if (discovered.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("--- Pass 2: re-lifting with " + discovered.Count + " discovered dynamic targets ---");

                if (!RunPass(rom, image, descriptor, supported, workDir, srcDir, safeName))
                {
                    Console.WriteLine("Pass 2 failed; the Pass 1 result is still valid.");
                    return true;
                }
            }
        }

        return true;
    }

    private bool RunPass(
        NesRom rom,
        ProgramImage image,
        MapperDescriptor descriptor,
        bool supported,
        string workDir,
        string srcDir,
        string safeName)
    {
        Disassembler dis = new Disassembler(rom, image);

        LoadDynamicTargets(workDir, dis);

        AnalysisResult model = dis.Analyze();

        int reachable = model.Instructions.Count;
        int codeBytes = 0;

        foreach (Instruction inst in model.Instructions.Values)
            codeBytes += inst.Length;

        Console.WriteLine(
            "Analysis: instructions=" + reachable +
            ", labels=" + model.Labels.Count +
            ", functions=" + model.Functions.Count +
            ", unknownOps=" + model.UnknownOpcodes.Count +
            ", indirectJumps=" + model.IndirectJumps.Count);

        Console.WriteLine(
            "Analysis: code bytes=" + codeBytes +
            " of " + image.PrgRom.Length +
            " (" + Percent(codeBytes, image.PrgRom.Length) + " of PRG)");

        WriteAnalysisReport(workDir, rom, descriptor, supported, model, codeBytes);

        Lifter lifter = new Lifter(rom, model, safeName);
        string code = lifter.Generate();

        if (_options.SaveSource)
        {
            string srcPath = Path.Combine(srcDir, "Game.generated.cs");
            File.WriteAllText(srcPath, code);
            Console.WriteLine("Generated C#: " + srcPath + " (" + Human(code.Length) + ")");
        }

        if (_options.NoCompile)
        {
            Console.WriteLine("Compilation disabled (--no-compile).");
            return true;
        }

        string dllPath = ResolveAssemblyPath(workDir, safeName);

        CompileResult result = _compiler.CompileDetailed(
            code,
            dllPath,
            message => Console.Error.WriteLine(message),
            Path.Combine("src", "Game.generated.cs"));

        if (!result.Success)
        {
            Console.Error.WriteLine("Roslyn compilation failed.");
            return false;
        }

        WriteRuntimeConfig(workDir, Path.GetFileName(dllPath));

        Console.WriteLine("Compiled: " + result.AssemblyPath + " (" + Human(new FileInfo(result.AssemblyPath).Length) + ")");

        if (_options.Verbose)
        {
            Console.WriteLine("PDB: " + result.PdbPath);
            Console.WriteLine("Diagnostics: " + result.WarningCount + " warning(s)");
        }

        Console.WriteLine("Run with: dotnet \"" + result.AssemblyPath + "\"");

        return true;
    }

    /// <summary>
    /// Returns a writable path for the generated assembly. When the previous
    /// build is still running, a timestamped side-by-side build is written
    /// instead of failing, which keeps the hot-reload workflow alive.
    /// </summary>
    private static string ResolveAssemblyPath(string workDir, string safeName)
    {
        string dllPath = Path.Combine(workDir, safeName + ".dll");

        if (!File.Exists(dllPath))
            return dllPath;

        try
        {
            File.Delete(dllPath);
            return dllPath;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            string suffix = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            string hotReload = Path.Combine(workDir, safeName + ".rebuild-" + suffix + ".dll");
            Console.WriteLine("Previous build is in use; writing a hot-reload build: " + hotReload);
            return hotReload;
        }
    }

    private static List<ushort> ReadDynamicTargets(string path)
    {
        List<ushort> targets = new List<ushort>();

        if (!File.Exists(path))
            return targets;

        try
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (TryParseAddress(rawLine, out ushort addr) && !targets.Contains(addr))
                    targets.Add(addr);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to read dynamic targets: " + ex.Message);
        }

        return targets;
    }

    private static bool TryParseAddress(string rawLine, out ushort address)
    {
        address = 0;
        string line = rawLine.Trim();

        if (line.Length == 0)
            return false;

        if (line.StartsWith(';') || line.StartsWith('#'))
            return false;

        // Tolerate a trailing comment: "8231 ; title screen".
        int comment = line.IndexOfAny(new[] { ';', '#' });

        if (comment >= 0)
            line = line.Substring(0, comment).Trim();

        if (line.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            line = line.Substring(2);

        line = line.Replace("$", string.Empty).Trim();

        return ushort.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    private static void EnsureDynamicTargetsFile(string workDir)
    {
        string path = Path.Combine(workDir, "dynamic_targets.txt");

        if (File.Exists(path))
            return;

        string[] lines =
        {
            "; NesLifter forced code addresses.",
            ";",
            "; Addresses listed here are disassembled even when the static",
            "; analysis cannot prove they are reachable. Use this for code that",
            "; is only entered through computed jumps, or for data that the",
            "; analyser wrongly treats as code.",
            ";",
            "; Accepted formats: 8231 / 0x8231 / $8231",
            "; Trailing comments with ; or # are ignored.",
            string.Empty
        };

        File.WriteAllLines(path, lines);
    }

    private static void LoadDynamicTargets(string workDir, Disassembler dis)
    {
        string[] targetFiles =
        {
            Path.Combine(workDir, "dynamic_targets.txt"),
            Path.Combine(workDir, "dynamic_targets.log")
        };

        foreach (string path in targetFiles)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    if (!TryParseAddress(rawLine, out ushort addr))
                        continue;

                    if (!dis.ForcedAddresses.Contains(addr))
                        dis.ForcedAddresses.Add(addr);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to read dynamic targets file: " + ex.Message);
            }
        }

        if (dis.ForcedAddresses.Count > 0)
            Console.WriteLine("Forced dynamic targets: " + dis.ForcedAddresses.Count);
    }

    private static void WriteAnalysisReport(
        string workDir,
        NesRom rom,
        MapperDescriptor descriptor,
        bool supported,
        AnalysisResult model,
        int codeBytes)
    {
        CartInfo cart = rom.Cart;

        List<string> lines = new List<string>
        {
            "NesLifter analysis report",
            "=========================",
            string.Empty,
            "[Cartridge]",
            "PRG ROM        : " + cart.PrgRomSize + " bytes",
            "CHR ROM        : " + cart.ChrRomSize + " bytes",
            "Mapper         : " + descriptor.Id + " (" + descriptor.Name + ")",
            "Mapper support : " + (supported ? "full" : "fallback (flat PRG layout)"),
            "Board summary  : " + descriptor.Summary,
            "Submapper      : " + cart.Submapper,
            "iNES 2.0       : " + cart.INes2,
            "Mirroring      : " + MirroringName(cart.Mirroring),
            "Battery        : " + cart.Battery,
            "Region         : " + RegionName(cart.Region),
            "PRG RAM        : " + cart.PrgRamSize + " + " + cart.PrgRamSaveSize + " (save)",
            "CHR RAM        : " + cart.ChrRamSize + " + " + cart.ChrRamSaveSize + " (save)",
            "CRC32 PRG      : 0x" + cart.PrgCrc32.ToString("X8"),
            "CRC32 CHR      : 0x" + cart.ChrCrc32.ToString("X8"),
            "CRC32 combined : 0x" + cart.Crc32.ToString("X8"),
            string.Empty,
            "[Vectors]",
            "NMI   : 0x" + rom.ReadVector(0xFFFA).ToString("X4"),
            "RESET : 0x" + rom.ReadVector(0xFFFC).ToString("X4"),
            "IRQ   : 0x" + rom.ReadVector(0xFFFE).ToString("X4"),
            string.Empty,
            "[Analysis]",
            "Entry         : 0x" + model.Entry.ToString("X4"),
            "Instructions  : " + model.Instructions.Count,
            "Code bytes    : " + codeBytes + " (" + Percent(codeBytes, cart.PrgRomSize) + " of PRG)",
            "Labels        : " + model.Labels.Count,
            "Functions     : " + model.Functions.Count,
            "Unknown ops   : " + model.UnknownOpcodes.Count,
            "Indirect jumps: " + model.IndirectJumps.Count,
            "Dynamic target: " + model.DynamicTargets.Count,
            string.Empty
        };

        lines.Add("[Functions]");
        AppendAddressList(lines, model.Functions, 512);

        lines.Add(string.Empty);
        lines.Add("[Unknown opcodes]");

        if (model.UnknownOpcodes.Count == 0)
        {
            lines.Add("none");
        }
        else
        {
            foreach (byte opcode in model.UnknownOpcodes)
                lines.Add("0x" + opcode.ToString("X2"));
        }

        lines.Add(string.Empty);
        lines.Add("[Indirect jumps]");
        AppendAddressList(lines, model.IndirectJumps, 512);

        string reportPath = Path.Combine(workDir, "analysis.txt");
        File.WriteAllLines(reportPath, lines);

        Console.WriteLine("Analysis report: " + reportPath);
    }

    private static void AppendAddressList(List<string> lines, List<ushort> addresses, int limit)
    {
        if (addresses.Count == 0)
        {
            lines.Add("none");
            return;
        }

        int written = 0;

        foreach (ushort address in addresses)
        {
            if (written >= limit)
            {
                lines.Add("... " + (addresses.Count - limit) + " more");
                break;
            }

            lines.Add("0x" + address.ToString("X4"));
            written++;
        }
    }

    private static void WriteRuntimeConfig(string workDir, string assemblyFileName)
    {
        int major = Environment.Version.Major;
        string tfm = "net" + major + ".0-windows";
        string version = major + ".0.0";

        string json =
            "{\n" +
            "  \"runtimeOptions\": {\n" +
            $"    \"tfm\": \"{tfm}\",\n" +
            "    \"rollForward\": \"LatestMajor\",\n" +
            "    \"frameworks\": [\n" +
            "      {\n" +
            "        \"name\": \"Microsoft.NETCore.App\",\n" +
            $"        \"version\": \"{version}\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"name\": \"Microsoft.WindowsDesktop.App\",\n" +
            $"        \"version\": \"{version}\"\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}\n";

        string path = Path.Combine(
            workDir,
            Path.GetFileNameWithoutExtension(assemblyFileName) + ".runtimeconfig.json");

        File.WriteAllText(path, json);
    }

    private static string MirroringName(int mirroring)
    {
        return mirroring switch
        {
            0 => "horizontal",
            1 => "vertical",
            2 => "four-screen",
            0x10 => "one-screen (low)",
            0x11 => "one-screen (high)",
            _ => "unknown (" + mirroring + ")"
        };
    }

    private static string RegionName(int region)
    {
        return region switch
        {
            0 => "NTSC",
            1 => "PAL",
            2 => "multi-region",
            3 => "Dendy",
            _ => "unknown (" + region + ")"
        };
    }

    private static string Percent(int part, int total)
    {
        if (total <= 0)
            return "0%";

        double percent = part * 100.0 / total;
        return percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Human(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";

        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";

        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }
}
