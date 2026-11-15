using System;
using System.Reflection;

namespace NesLifter.Host;

internal static class Program
{
    /// <summary>
    /// Entry point.
    ///
    /// STA is mandatory: WebView2 (and WinForms in general) require a
    /// single-threaded apartment, and initialising COM as MTA first makes every
    /// later WebView2 attempt fail with RPC_E_CHANGED_MODE.
    /// </summary>
    [STAThread]
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

        if (options.Gui)
            return RunGui(options);

        foreach (string unknown in options.UnknownArgs)
        {
            Console.Error.WriteLine("Unknown argument: " + unknown);
        }

        // No explicit arguments in a real terminal: offer the interactive UI.
        // Explicit arguments always win, so scripts and CI behave exactly as
        // before and never block on a prompt.
        if (args.Length == 0 && !options.NoTui)
            return RunInteractive(options);
        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            PrintHelp();
            return 1;
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

    /// <summary>
    /// Starts the WebView2 GUI. Falls back to the terminal UI when the WebView2
    /// runtime is missing or cannot initialise, so a machine without Edge (or a
    /// remote session without GPU support) still has a usable interface.
    /// </summary>
    private static int RunGui(CliOptions options)
    {
        Tui.TuiState state = BuildState(options);

        if (!string.IsNullOrWhiteSpace(options.InputPath))
            state.InputPath = options.InputPath;

        string? startupError = null;

        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            using Gui.GuiHost host = new Gui.GuiHost(state);
            System.Windows.Forms.Application.Run(host);

            startupError = host.StartupError;
        }
        catch (Exception ex)
        {
            startupError = ex.Message;
        }

        if (string.IsNullOrEmpty(startupError))
            return 0;

        Console.Error.WriteLine("Graphical interface unavailable: " + startupError);
        Console.Error.WriteLine("Falling back to the terminal interface.");
        Console.Error.WriteLine();

        return RunInteractive(options);
    }

    /// <summary>
    /// Starts the TUI, seeding it from any defaults the user configured.
    /// Falls back to line mode when the console cannot host a full-screen UI.
    /// </summary>
    private static int RunInteractive(CliOptions options)
    {
        Tui.TuiState state = BuildState(options);

        Tui.TuiApp app = new Tui.TuiApp(state);

        try
        {
            return app.Run();
        }
        catch (Exception ex)
        {
            // A terminal that cannot host the TUI must not lose the tool.
            Console.Error.WriteLine("Interactive UI failed: " + ex.Message);
            Console.Error.WriteLine("Falling back to the command line. Run with --help for options.");
            return 1;
        }
    }

    private static Tui.TuiState BuildState(CliOptions options)
    {
        return new Tui.TuiState
        {
            OutputPath = options.OutputPath,

            // A ROM library is always nested, so the interactive UIs default to
            // recursive. An explicit -r still wins, and the user can flip it in
            // the UI either way.
            Recursive = options.RecursiveSpecified ? options.Recursive : true,

            SaveSource = options.SaveSource,
            PackageMode = options.PackageMode,
            CaptureIconSeconds = options.CaptureIconSeconds,
            IconPath = options.IconPath
        };
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
        Console.WriteLine("  NesLifter                     interactive UI (GUI if available, else TUI)");
        Console.WriteLine("  NesLifter <rom.nes|folder> [options]");
        Console.WriteLine("  NesLifter --selftest [options]");
        Console.WriteLine();
        Console.WriteLine("Interface:");
        Console.WriteLine("      --gui               Force the WebView2 graphical interface.");
        Console.WriteLine("      --no-tui, --cli     Never start an interactive UI; use arguments only.");
        Console.WriteLine();
        Console.WriteLine("Input / output:");
        Console.WriteLine("  -i, --input <path>      Input .nes file or folder with ROMs.");
        Console.WriteLine("  -o, --output <dir>      Output directory. Default: nes_lifted_output");
        Console.WriteLine("  -r, --recursive         Recurse into sub-folders.");
        Console.WriteLine();
        Console.WriteLine("Lifting:");
        Console.WriteLine("      --no-compile        Generate C# only, skip compilation.");
        Console.WriteLine("      --no-source         Do not keep the generated C# source.");
        Console.WriteLine("      --keep-source       Keep the generated C# source (default).");
        Console.WriteLine("  -v, --verbose           Verbose pipeline output.");
        Console.WriteLine();
        Console.WriteLine("Packaging:");
        Console.WriteLine("      --exe               Game.exe + Game.dll (default). Needs .NET installed.");
        Console.WriteLine("      --dll               Only Game.dll, run with: dotnet Game.dll");
        Console.WriteLine("      --full-exe          Self-contained single .exe, no .NET needed (~50 MB).");
        Console.WriteLine("                          Aliases: --self-contained, --standalone");
        Console.WriteLine();
        Console.WriteLine("Icon:");
        Console.WriteLine("      --icon <image>      Use this image as the game icon (any format).");
        Console.WriteLine("      --capture-icon <n>  Let the game screenshot itself after n seconds.");
        Console.WriteLine("                          Default: 5. The captured icon is reused on re-lift.");
        Console.WriteLine("      --no-icon           Do not capture a screenshot icon.");
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
        Console.WriteLine("  <out>/<RomName>/<RomName>.exe          double-click to play (default)");
        Console.WriteLine("  <out>/<RomName>/<RomName>.dll          lifted game assembly");
        Console.WriteLine("  <out>/<RomName>/<RomName>.icon.ico     icon captured from the running game");
        Console.WriteLine("  <out>/<RomName>/src/Game.generated.cs  generated C# (readable)");
        Console.WriteLine("  <out>/<RomName>/analysis.txt           mapper, vectors, coverage");
        Console.WriteLine("  <out>/<RomName>/dynamic_targets.txt    manually forced code addresses");
        Console.WriteLine();
        Console.WriteLine("Run the result with:");
        Console.WriteLine("  Game.exe");
        Console.WriteLine("  Game.exe --headless 600          (no window, CI friendly)");
        Console.WriteLine("  Game.exe --capture-icon 5        (screenshot itself as the icon)");
        Console.WriteLine();
    }
}
