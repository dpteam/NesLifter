using System;
using System.Reflection;

namespace NesLifter.Host;

internal static class Program
{
    private static int Main(string[] args)
    {
        CliOptions options = CliOptions.Parse(args);

        if (options.ShowVersion)
        {
            PrintVersion();
            return 0;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.RunSelfTest)
        {
            return new SelfTest(options).Run();
        }

        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            PrintHelp();
            return 1;
        }

        foreach (string unknown in options.UnknownArgs)
        {
            Console.Error.WriteLine("Unknown argument: " + unknown);
        }

        try
        {
            Pipeline pipeline = new Pipeline(options);
            return pipeline.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error:");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void PrintVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine("NesLifter " + (version?.ToString(3) ?? "0.0.0"));
        Console.WriteLine("Runtime: " + Environment.Version);
        Console.WriteLine("Mappers: " + string.Join(", ", NesLifter.Core.Boards.BoardFactory.SupportedMappers));
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("NesLifter - static NES (6502) recompiler to .NET");
        Console.WriteLine("================================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  NesLifter <rom.nes|folder> [options]");
        Console.WriteLine("  NesLifter --selftest [options]");
        Console.WriteLine();
        Console.WriteLine("Input / output:");
        Console.WriteLine("  -i, --input <path>      Input .nes file or folder with ROMs.");
        Console.WriteLine("  -o, --output <dir>      Output directory. Default: nes_lifted_output");
        Console.WriteLine("  -r, --recursive         Recurse into sub-folders.");
        Console.WriteLine();
        Console.WriteLine("Lifting:");
        Console.WriteLine("      --no-compile        Generate C# only, skip Roslyn compilation.");
        Console.WriteLine("      --no-source         Do not keep the generated C# source.");
        Console.WriteLine("      --keep-source       Keep the generated C# source (default).");
        Console.WriteLine("  -v, --verbose           Verbose pipeline output.");
        Console.WriteLine();
        Console.WriteLine("Verification:");
        Console.WriteLine("      --selftest          Lift, compile and execute synthetic ROMs for");
        Console.WriteLine("                          every supported mapper. Needs no ROM files.");
        Console.WriteLine("      --frames <n>         Frame budget for the headless run. Default: 30");
        Console.WriteLine("      --no-run            Compile but do not execute during --selftest.");
        Console.WriteLine("      --keep              Keep --selftest artifacts.");
        Console.WriteLine();
        Console.WriteLine("Other:");
        Console.WriteLine("      --version           Print version and supported mappers.");
        Console.WriteLine("  -h, --help              Show this help.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  NesLifter game.nes");
        Console.WriteLine("  NesLifter -i C:\\roms -o C:\\lifted -r");
        Console.WriteLine("  NesLifter --selftest");
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine("  <out>/<RomName>/<RomName>.dll        lifted game (dotnet <dll>)");
        Console.WriteLine("  <out>/<RomName>/<RomName>.runtimeconfig.json");
        Console.WriteLine("  <out>/<RomName>/src/Game.generated.cs  generated C# (readable)");
        Console.WriteLine("  <out>/<RomName>/analysis.txt           mapper, vectors, coverage");
        Console.WriteLine("  <out>/<RomName>/dynamic_targets.txt    manually forced code addresses");
        Console.WriteLine();
        Console.WriteLine("Run the result with:");
        Console.WriteLine("  dotnet <RomName>.dll");
        Console.WriteLine("  dotnet <RomName>.dll --headless 600   (no window, CI friendly)");
        Console.WriteLine();
    }
}
