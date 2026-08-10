using System;

namespace NesLifter.Host;

internal static class Program
{
    private static int Main(string[] args)
    {
        CliOptions options = CliOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            PrintHelp();
            return 1;
        }

        foreach (string unknown in options.UnknownArgs)
        {
            Console.Error.WriteLine($"Unknown argument: {unknown}");
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

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("NES Static Recompiler / Lifter - modern .NET host");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  NesLifter --input <rom.nes|folder> --output <dir> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>      Input .nes file or folder.");
        Console.WriteLine("  -o, --output <dir>      Output directory. Default: nes_lifted_output");
        Console.WriteLine("  -r, --recursive         Recursive folder processing.");
        Console.WriteLine("      --no-compile        Only generate C#, skip Roslyn compilation.");
        Console.WriteLine("      --no-source         Do not save generated C# source.");
        Console.WriteLine("      --keep-source       Save generated C# source (default).");
        Console.WriteLine("  -h, --help              Show help.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  NesLifter game.nes");
        Console.WriteLine("  NesLifter -i C:\\roms -o C:\\lifted -r");
        Console.WriteLine("  NesLifter -i C:\\roms -r --no-compile");
        Console.WriteLine();
        Console.WriteLine("Note:");
        Console.WriteLine("  Modern .NET Roslyn compilation produces an IL assembly (.dll).");
        Console.WriteLine("  Run result with: dotnet <generated.dll>");
        Console.WriteLine();
    }
}