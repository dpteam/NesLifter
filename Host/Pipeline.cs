using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NesLifter.Common;
using NesLifter.Compilation;
using NesLifter.Core;
using NesLifter.Core.Boards;
using NesLifter.Lifting;

namespace NesLifter.Host
{
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

            // === Board ===
            CartMapping mapping = new CartMapping();
            IBoard board = null;

            try
            {
                board = BoardFactory.Create(cart, mapping);
                board.Power();
                Console.WriteLine("Board: Mapper " + cart.Mapper + " initialized, Power() called.");
            }
            catch (NotSupportedException ex)
            {
                Console.WriteLine("Board: " + ex.Message);
                Console.WriteLine("Board: Continuing without board support.");
            }

            // === Pass 1 ===
            EnsureDynamicTargetsFile(workDir);

            Console.WriteLine("--- Pass 1: Initial analysis ---");
            bool pass1 = RunPass(rom, mapping, board, workDir, srcDir, safeName);

            if (!pass1)
                return false;

            if (_options.NoCompile)
                return true;

            // === Pass 2: check if new dynamic targets appeared ===
            string dynLog = Path.Combine(workDir, "dynamic_targets.log");

            if (File.Exists(dynLog))
            {
                List<ushort> newTargets = ReadDynamicTargetsFromFile(dynLog);

                if (newTargets.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("--- Pass 2: Re-lifting with " + newTargets.Count + " discovered dynamic targets ---");

                    bool pass2 = RunPass(rom, mapping, board, workDir, srcDir, safeName);

                    if (!pass2)
                    {
                        Console.WriteLine("Pass 2 failed, but Pass 1 result is still valid.");
                        return true;
                    }
                }
            }

            return true;
        }

        private bool RunPass(
    NesRom rom,
    CartMapping mapping,
    IBoard board,
    string workDir,
    string srcDir,
    string safeName)
        {
            Disassembler dis = new Disassembler(rom);

            LoadDynamicTargets(workDir, dis);

            AnalysisResult model = dis.Analyze();

            Console.WriteLine(
                "Analysis: instructions=" + model.Instructions.Count +
                ", labels=" + model.Labels.Count +
                ", functions=" + model.Functions.Count +
                ", unknownOps=" + model.UnknownOpcodes.Count +
                ", indirectJumps=" + model.IndirectJumps.Count);

            WriteAnalysisReport(workDir, "", rom, model);

            Lifter lifter = new Lifter(rom, model, safeName, mapping, board);
            string code = lifter.Generate();

            if (_options.SaveSource)
            {
                string srcPath = Path.Combine(srcDir, "Game.generated.cs");
                File.WriteAllText(srcPath, code);
                Console.WriteLine("Generated C# saved: " + srcPath);
            }

            if (_options.NoCompile)
            {
                Console.WriteLine("Compilation disabled (--no-compile).");
                return true;
            }

            string dllPath = Path.Combine(workDir, safeName + ".dll");

            if (File.Exists(dllPath))
            {
                try { File.Delete(dllPath); }
                catch { }
            }

            bool compiled = _compiler.Compile(code, dllPath, message =>
            {
                Console.Error.WriteLine(message);
            });

            if (!compiled)
            {
                Console.Error.WriteLine("Roslyn compilation failed.");
                return false;
            }

            WriteRuntimeConfig(workDir, safeName + ".dll");

            Console.WriteLine("Compiled: " + dllPath);
            Console.WriteLine("Run with: dotnet \"" + dllPath + "\"");

            return true;
        }

        private static List<ushort> ReadDynamicTargetsFromFile(string path)
        {
            List<ushort> targets = new List<ushort>();

            if (!File.Exists(path))
                return targets;

            try
            {
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();

                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        line = line.Substring(2);

                    line = line.Replace("$", "").Trim();

                    ushort addr;
                    if (ushort.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                    {
                        if (!targets.Contains(addr))
                            targets.Add(addr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to read dynamic targets: " + ex.Message);
            }

            return targets;
        }

        private static void EnsureDynamicTargetsFile(string workDir)
        {
            string path = Path.Combine(workDir, "dynamic_targets.txt");

            if (File.Exists(path))
                return;

            string[] lines = new string[]
            {
                "; Hex addresses for forced disassembly.",
                "; Formats:",
                "; 8231",
                "; 0x8231",
                "; $8231",
                ""
            };

            File.WriteAllLines(path, lines);
        }

        private static void LoadDynamicTargets(string workDir, Disassembler dis)
        {
            string[] targetFiles = new string[]
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
                        string line = rawLine.Trim();

                        if (line.Length == 0)
                            continue;

                        if (line.StartsWith(";") || line.StartsWith("#"))
                            continue;

                        if (line.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            line = line.Substring(2);

                        line = line.Replace("$", "").Trim();

                        ushort addr;
                        if (ushort.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                        {
                            if (!dis.ForcedAddresses.Contains(addr))
                                dis.ForcedAddresses.Add(addr);
                        }
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
            string sourceFile,
            NesRom rom,
            AnalysisResult model)
        {
            CartInfo cart = rom.Cart;

            List<string> lines = new List<string>();

            lines.Add("NesLifter analysis report");
            lines.Add("File: " + sourceFile);
            lines.Add("PRG ROM: " + cart.PrgRomSize + " bytes");
            lines.Add("CHR ROM: " + cart.ChrRomSize + " bytes");
            lines.Add("Mapper: " + cart.Mapper);
            lines.Add("Submapper: " + cart.Submapper);
            lines.Add("iNES 2.0: " + cart.INes2);
            lines.Add("Mirroring: " + cart.Mirroring);
            lines.Add("Battery: " + cart.Battery);
            lines.Add("Region: " + cart.Region);
            lines.Add("PRG RAM: " + cart.PrgRamSize + " + " + cart.PrgRamSaveSize + " (save)");
            lines.Add("CHR RAM: " + cart.ChrRamSize + " + " + cart.ChrRamSaveSize + " (save)");
            lines.Add("CRC32 PRG: 0x" + cart.PrgCrc32.ToString("X8"));
            lines.Add("CRC32 CHR: 0x" + cart.ChrCrc32.ToString("X8"));
            lines.Add("CRC32 Combined: 0x" + cart.Crc32.ToString("X8"));
            lines.Add("");
            lines.Add("NMI vector: 0x" + rom.ReadVector(0xFFFA).ToString("X4"));
            lines.Add("RESET vector: 0x" + rom.ReadVector(0xFFFC).ToString("X4"));
            lines.Add("IRQ vector: 0x" + rom.ReadVector(0xFFFE).ToString("X4"));
            lines.Add("");
            lines.Add("Entry: 0x" + model.Entry.ToString("X4"));
            lines.Add("Instructions: " + model.Instructions.Count);
            lines.Add("Labels: " + model.Labels.Count);
            lines.Add("Functions: " + model.Functions.Count);
            lines.Add("Unknown opcodes: " + model.UnknownOpcodes.Count);
            lines.Add("Indirect jumps: " + model.IndirectJumps.Count);
            lines.Add("Dynamic targets: " + model.DynamicTargets.Count);
            lines.Add("");

            lines.Add("Functions:");
            int maxFunctions = 256;
            int functionCount = 0;

            foreach (ushort address in model.Functions)
            {
                if (functionCount >= maxFunctions)
                {
                    lines.Add("...");
                    break;
                }

                lines.Add("0x" + address.ToString("X4"));
                functionCount++;
            }

            lines.Add("");
            lines.Add("Unknown opcodes:");

            foreach (byte opcode in model.UnknownOpcodes)
                lines.Add("0x" + opcode.ToString("X2"));

            lines.Add("");
            lines.Add("Indirect jumps:");

            foreach (ushort address in model.IndirectJumps)
                lines.Add("0x" + address.ToString("X4"));

            string reportPath = Path.Combine(workDir, "analysis.txt");
            File.WriteAllLines(reportPath, lines);

            Console.WriteLine("Analysis report saved: " + reportPath);
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
    }
}