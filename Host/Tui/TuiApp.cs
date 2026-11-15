using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using NesLifter.Compilation;

namespace NesLifter.Host.Tui;

/// <summary>
/// Interactive terminal UI.
///
/// Launched when NesLifter runs without arguments in a real terminal. Explicit
/// command-line arguments always win and skip the TUI entirely, so scripts and
/// CI keep working exactly as before.
///
/// The UI is a small state machine over a handful of screens; there is no
/// framework, because a recompiler front end needs to start instantly and run
/// anywhere a console exists.
/// </summary>
public sealed class TuiApp
{
    private enum Screen
    {
        Menu,
        Browser,
        Options,
        Lifting,
        Results
    }

    private readonly TuiState _state;
    private readonly LogBuffer _log = new(2000);

    private Screen _screen = Screen.Menu;
    private int _menuIndex;
    private int _browserIndex;
    private int _browserScroll;
    private int _optionIndex;
    private List<RomEntry> _roms = new();
    private readonly HashSet<int> _selected = new();
    private string _status = string.Empty;
    private bool _quit;

    private Thread? _worker;
    private volatile bool _working;
    private string _liftTarget = string.Empty;
    private int _liftIndex;
    private int _liftTotal;
    private int _liftOk;
    private int _liftFailed;
    private readonly Stopwatch _liftClock = new();

    private static readonly string[] MenuItems =
    {
        "Pick ROM file or folder",
        "Scan for cartridges",
        "Lift options",
        "Run self-test",
        "Quit"
    };

    public TuiApp(TuiState state)
    {
        _state = state;
    }

    /// <summary>Runs the TUI until the user quits. Returns a process exit code.</summary>
    public int Run()
    {
        if (!ConsoleUi.IsInteractive)
            return RunPlainFallback();

        try
        {
            while (!_quit)
            {
                switch (_screen)
                {
                    case Screen.Menu: DrawMenu(); break;
                    case Screen.Browser: DrawBrowser(); break;
                    case Screen.Options: DrawOptions(); break;
                    case Screen.Lifting: DrawLifting(); break;
                    case Screen.Results: DrawResults(); break;
                }
            }
        }
        finally
        {
            ConsoleUi.Reset();
        }

        return _liftFailed > 0 ? 2 : 0;
    }

    // =================================================================
    // Screens
    // =================================================================

    private void DrawMenu()
    {
        ConsoleUi.Clear();
        DrawHeader("NES static recompiler");

        Console.WriteLine();

        for (int i = 0; i < MenuItems.Length; i++)
        {
            bool active = i == _menuIndex;
            string prefix = active ? "  \u25B6 " : "    ";

            ConsoleUi.Write(prefix, ConsoleUi.Red);
            ConsoleUi.WriteLine(MenuItems[i], active ? ConsoleUi.Cream : ConsoleUi.Ink);
        }

        Console.WriteLine();
        DrawStateSummary();
        DrawStatus();
        DrawFooter("\u2191\u2193 move   Enter select   Q quit");

        ConsoleKeyInfo? key = ConsoleUi.ReadKey();

        if (key == null)
        {
            _quit = true;
            return;
        }

        switch (key.Value.Key)
        {
            case ConsoleKey.UpArrow:
                _menuIndex = (_menuIndex + MenuItems.Length - 1) % MenuItems.Length;
                break;

            case ConsoleKey.DownArrow:
                _menuIndex = (_menuIndex + 1) % MenuItems.Length;
                break;

            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _quit = true;
                break;

            case ConsoleKey.Enter:
                ActivateMenu();
                break;
        }
    }

    private void ActivateMenu()
    {
        switch (_menuIndex)
        {
            case 0:
                PromptForPath();
                break;

            case 1:
                ScanCurrentPath();
                break;

            case 2:
                _screen = Screen.Options;
                break;

            case 3:
                RunSelfTest();
                break;

            case 4:
                _quit = true;
                break;
        }
    }

    private void DrawBrowser()
    {
        ConsoleUi.Clear();
        DrawHeader("Cartridges");

        int rows = ConsoleUi.Height - 10;
        int count = _roms.Count;

        if (count == 0)
        {
            Console.WriteLine();
            ConsoleUi.WriteLine("  No cartridges found in this path.", ConsoleUi.Gold);
            ConsoleUi.WriteLine("  Press B to pick another path.", ConsoleUi.Muted);
            DrawFooter("B change path   Esc back   Q quit");

            ConsoleKeyInfo? emptyKey = ConsoleUi.ReadKey();

            if (emptyKey == null) { _quit = true; return; }

            if (emptyKey.Value.Key == ConsoleKey.B) PromptForPath();
            else if (emptyKey.Value.Key is ConsoleKey.Escape or ConsoleKey.Q) _screen = Screen.Menu;

            return;
        }

        // Keep the cursor on screen.
        if (_browserIndex < _browserScroll)
            _browserScroll = _browserIndex;
        else if (_browserIndex >= _browserScroll + rows)
            _browserScroll = _browserIndex - rows + 1;

        Console.WriteLine();
        DrawBrowserHeader();

        for (int i = 0; i < rows; i++)
        {
            int index = _browserScroll + i;

            if (index >= count)
            {
                Console.WriteLine();
                continue;
            }

            DrawBrowserRow(index, _roms[index], index == _browserIndex);
        }

        Console.WriteLine();
        DrawStatus();
        DrawFooter("\u2191\u2193 move   Space select   A all   Enter lift   O options   Esc back");

        ConsoleKeyInfo? listKey = ConsoleUi.ReadKey();

        if (listKey == null) { _quit = true; return; }

        switch (listKey.Value.Key)
        {
            case ConsoleKey.UpArrow:
                _browserIndex = (_browserIndex + count - 1) % count;
                break;

            case ConsoleKey.DownArrow:
                _browserIndex = (_browserIndex + 1) % count;
                break;

            case ConsoleKey.PageUp:
                _browserIndex = Math.Max(0, _browserIndex - rows);
                break;

            case ConsoleKey.PageDown:
                _browserIndex = Math.Min(count - 1, _browserIndex + rows);
                break;

            case ConsoleKey.Spacebar:
                if (!_selected.Remove(_browserIndex))
                    _selected.Add(_browserIndex);

                break;

            case ConsoleKey.A:
                if (_selected.Count == count) _selected.Clear();
                else for (int i = 0; i < count; i++) _selected.Add(i);
                break;

            case ConsoleKey.O:
                _screen = Screen.Options;
                break;

            case ConsoleKey.Escape:
                _screen = Screen.Menu;
                break;

            case ConsoleKey.Q:
                _quit = true;
                break;

            case ConsoleKey.Enter:
                StartLift(SelectedEntries());
                break;
        }
    }

    private void DrawOptions()
    {
        ConsoleUi.Clear();
        DrawHeader("Lift options");

        Console.WriteLine();

        string[] labels =
        {
            "Package",
            "Icon from screenshot",
            "Custom icon",
            "Output directory",
            "Recursive scan",
            "Keep generated C#"
        };

        for (int i = 0; i < labels.Length; i++)
        {
            bool active = i == _optionIndex;

            ConsoleUi.Write(active ? "  \u25B6 " : "    ", ConsoleUi.Red);
            ConsoleUi.Write(ConsoleUi.Fit(labels[i], 26), active ? ConsoleUi.Cream : ConsoleUi.Ink);
            ConsoleUi.WriteLine(OptionValue(i), ConsoleUi.Gold);
        }

        Console.WriteLine();
        ConsoleUi.WriteLine("  Left/Right changes a value, Enter edits a path.", ConsoleUi.Muted);
        DrawFooter("\u2191\u2193 move   \u2190\u2192 change   Enter edit   Esc back");

        ConsoleKeyInfo? optionKey = ConsoleUi.ReadKey();

        if (optionKey == null) { _quit = true; return; }

        switch (optionKey.Value.Key)
        {
            case ConsoleKey.UpArrow:
                _optionIndex = (_optionIndex + labels.Length - 1) % labels.Length;
                break;

            case ConsoleKey.DownArrow:
                _optionIndex = (_optionIndex + 1) % labels.Length;
                break;

            case ConsoleKey.LeftArrow:
                CycleOption(-1);
                break;

            case ConsoleKey.RightArrow:
                CycleOption(1);
                break;

            case ConsoleKey.Enter:
                EditOption();
                break;

            case ConsoleKey.Escape:
                _screen = _roms.Count > 0 ? Screen.Browser : Screen.Menu;
                break;
        }
    }

    private string OptionValue(int index)
    {
        return index switch
        {
            0 => _state.PackageMode switch
            {
                PackageMode.AssemblyOnly => "dll only",
                PackageMode.AppHost => "exe + dll (needs .NET)",
                _ => "full exe (self-contained)"
            },
            1 => _state.CaptureIconSeconds <= 0 ? "off" : _state.CaptureIconSeconds + " s",
            2 => _state.IconPath ?? "none",
            3 => _state.OutputPath,
            4 => _state.Recursive ? "on" : "off",
            _ => _state.SaveSource ? "yes" : "no"
        };
    }

    private void CycleOption(int direction)
    {
        switch (_optionIndex)
        {
            case 0:
                _state.PackageMode = (PackageMode)(((int)_state.PackageMode + direction + 3) % 3);
                break;

            case 1:
                int[] steps = { 0, 2, 3, 5, 10, 15 };
                int at = Array.IndexOf(steps, _state.CaptureIconSeconds);

                if (at < 0) at = 3;

                at = (at + direction + steps.Length) % steps.Length;
                _state.CaptureIconSeconds = steps[at];
                break;

            case 4:
                _state.Recursive = !_state.Recursive;
                break;

            case 5:
                _state.SaveSource = !_state.SaveSource;
                break;
        }
    }

    private void EditOption()
    {
        switch (_optionIndex)
        {
            case 2:
                _state.IconPath = PromptPath("Icon image", _state.IconPath);
                break;

            case 3:
                _state.OutputPath = PromptPath("Output directory", _state.OutputPath) ?? _state.OutputPath;
                break;
        }
    }

    private void DrawLifting()
    {
        ConsoleUi.Clear();
        DrawHeader("Lifting");

        Console.WriteLine();
        ConsoleUi.Write("  ", ConsoleUi.Ink);
        ConsoleUi.WriteLine(_liftTarget, ConsoleUi.Cream);

        // Progress bar.
        int width = Math.Max(10, ConsoleUi.Width - 24);
        double ratio = _liftTotal <= 0 ? 0 : (double)_liftIndex / _liftTotal;
        int filled = (int)Math.Round(ratio * width);

        Console.WriteLine();
        ConsoleUi.Write("  [", ConsoleUi.Muted);
        ConsoleUi.Write(new string('#', filled), ConsoleUi.Red);
        ConsoleUi.Write(new string('.', Math.Max(0, width - filled)), ConsoleUi.Muted);
        ConsoleUi.Write("] ", ConsoleUi.Muted);
        ConsoleUi.WriteLine($"{_liftIndex}/{_liftTotal}   ok {_liftOk}  failed {_liftFailed}   {_liftClock.Elapsed.TotalSeconds:0.0}s", ConsoleUi.Gold);

        Console.WriteLine();
        ConsoleUi.WriteLine("  Log", ConsoleUi.Accent);
        ConsoleUi.Rule('[', '-', ']', ConsoleUi.Muted);

        int rows = Math.Max(3, ConsoleUi.Height - 16);
        List<string> tail = _log.Tail(rows);

        foreach (string line in tail)
        {
            ConsoleColor color = ConsoleUi.Ink;

            if (line.Contains("ERROR", StringComparison.Ordinal) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                color = ConsoleUi.Red;
            else if (line.Contains("Packaged", StringComparison.Ordinal) || line.Contains("ok=1", StringComparison.Ordinal))
                color = ConsoleUi.Good;
            else if (line.StartsWith("===", StringComparison.Ordinal))
                color = ConsoleUi.Accent;

            ConsoleUi.WriteLine("  " + line, color);
        }

        for (int i = tail.Count; i < rows; i++)
            Console.WriteLine();

        DrawFooter(_working ? "working\u2026   Esc cancels after the current game" : "Enter continue");

        if (!_working)
        {
            ConsoleKeyInfo? key = ConsoleUi.ReadKey();

            if (key?.Key == ConsoleKey.Enter || key == null)
                _screen = Screen.Results;
        }
        else
        {
            Thread.Sleep(120);
        }
    }

    private void DrawResults()
    {
        ConsoleUi.Clear();
        DrawHeader("Results");

        Console.WriteLine();
        ConsoleUi.Write("  Lifted: ", ConsoleUi.Ink);
        ConsoleUi.WriteLine(_liftOk.ToString(CultureInfo.InvariantCulture), ConsoleUi.Good);
        ConsoleUi.Write("  Failed: ", ConsoleUi.Ink);
        ConsoleUi.WriteLine(_liftFailed.ToString(CultureInfo.InvariantCulture), _liftFailed > 0 ? ConsoleUi.Red : ConsoleUi.Muted);
        ConsoleUi.Write("  Time:   ", ConsoleUi.Ink);
        ConsoleUi.WriteLine(_liftClock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s", ConsoleUi.Gold);
        ConsoleUi.Write("  Output: ", ConsoleUi.Ink);
        ConsoleUi.WriteLine(Path.GetFullPath(_state.OutputPath), ConsoleUi.Cream);

        Console.WriteLine();
        ConsoleUi.WriteLine("  Built games", ConsoleUi.Accent);
        ConsoleUi.Rule('[', '-', ']', ConsoleUi.Muted);

        int rows = Math.Max(2, ConsoleUi.Height - 16);
        int shown = 0;

        foreach (RomEntry rom in _roms)
        {
            if (string.IsNullOrEmpty(rom.Result))
                continue;

            if (shown >= rows)
                break;

            ConsoleColor color = rom.Result.StartsWith("ok", StringComparison.Ordinal) ? ConsoleUi.Good : ConsoleUi.Red;

            ConsoleUi.Write("  ", ConsoleUi.Ink);
            ConsoleUi.Write(ConsoleUi.Fit(rom.FileName, ConsoleUi.Width - 22), ConsoleUi.Cream);
            ConsoleUi.WriteLine(rom.Result, color);
            shown++;
        }

        if (shown == 0)
            ConsoleUi.WriteLine("  nothing lifted yet", ConsoleUi.Muted);

        DrawFooter("O open output folder   Enter back to list   Q quit");

        ConsoleKeyInfo? key = ConsoleUi.ReadKey();

        if (key == null) { _quit = true; return; }

        switch (key.Value.Key)
        {
            case ConsoleKey.O:
                OpenOutputFolder();
                break;

            case ConsoleKey.Enter:
                _screen = _roms.Count > 0 ? Screen.Browser : Screen.Menu;
                break;

            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _quit = true;
                break;
        }
    }

    // =================================================================
    // Chrome
    // =================================================================

    private void DrawHeader(string subtitle)
    {
        ConsoleUi.WriteBar("  NESLIFTER  \u00B7  " + subtitle, ConsoleUi.Cream, ConsoleColor.DarkRed);
    }

    private void DrawBrowserHeader()
    {
        int nameWidth = Math.Max(20, ConsoleUi.Width - 34);

        ConsoleUi.Write("   " + ConsoleUi.Fit("cartridge", nameWidth), ConsoleUi.Muted);
        ConsoleUi.Write("  " + ConsoleUi.PadLeft("map", 4), ConsoleUi.Muted);
        ConsoleUi.Write("  " + ConsoleUi.Fit("board", 12), ConsoleUi.Muted);
        ConsoleUi.Write("  " + ConsoleUi.PadLeft("size", 6), ConsoleUi.Muted);
        Console.WriteLine();

        ConsoleUi.Rule('[', '-', ']', ConsoleUi.Muted);
    }

    private void DrawBrowserRow(int index, RomEntry rom, bool active)
    {
        int nameWidth = Math.Max(20, ConsoleUi.Width - 34);

        string mark = _selected.Contains(index) ? "[x]" : "[ ]";
        string name = ConsoleUi.Fit(rom.FileName, nameWidth);
        string mapper = rom.Mapper < 0 ? "  ?" : ConsoleUi.PadLeft(rom.Mapper.ToString(CultureInfo.InvariantCulture), 4);
        string board = ConsoleUi.Fit(rom.MapperName, 12);
        string size = ConsoleUi.PadLeft(rom.SizeLabel(), 6);

        ConsoleUi.Write(active ? " \u25B6 " : "   ", ConsoleUi.Red);
        ConsoleUi.Write(mark + " ", _selected.Contains(index) ? ConsoleUi.Gold : ConsoleUi.Muted);
        ConsoleUi.Write(name, active ? ConsoleUi.Cream : ConsoleUi.Ink);
        ConsoleUi.Write("  ", ConsoleUi.Ink);
        ConsoleUi.Write(mapper, rom.MapperSupported ? ConsoleUi.Good : ConsoleUi.Red);
        ConsoleUi.Write("  ", ConsoleUi.Ink);
        ConsoleUi.Write(board, rom.MapperSupported ? ConsoleUi.Ink : ConsoleUi.Red);
        ConsoleUi.Write("  ", ConsoleUi.Ink);

        if (!string.IsNullOrEmpty(rom.Result))
            ConsoleUi.WriteLine(size + " " + rom.Result, rom.Result.StartsWith("ok", StringComparison.Ordinal) ? ConsoleUi.Good : ConsoleUi.Red);
        else
            ConsoleUi.WriteLine(size, ConsoleUi.Muted);
    }

    private void DrawStateSummary()
    {
        ConsoleUi.Write("  Input:   ", ConsoleUi.Muted);
        ConsoleUi.WriteLine(string.IsNullOrEmpty(_state.InputPath) ? "(none)" : _state.InputPath, ConsoleUi.Cream);

        ConsoleUi.Write("  Output:  ", ConsoleUi.Muted);
        ConsoleUi.WriteLine(Path.GetFullPath(_state.OutputPath), ConsoleUi.Cream);

        ConsoleUi.Write("  Package: ", ConsoleUi.Muted);
        ConsoleUi.WriteLine(OptionValue(0), ConsoleUi.Gold);

        ConsoleUi.Write("  Icon:    ", ConsoleUi.Muted);
        ConsoleUi.WriteLine(_state.CaptureIconSeconds > 0 ? "from screenshot after " + _state.CaptureIconSeconds + " s" : "off", ConsoleUi.Gold);
    }

    private void DrawStatus()
    {
        if (string.IsNullOrEmpty(_status))
            return;

        Console.WriteLine();
        ConsoleUi.WriteLine("  " + _status, ConsoleUi.Gold);
    }

    private void DrawFooter(string keys)
    {
        try
        {
            int row = ConsoleUi.Height - 1;
            Console.SetCursorPosition(0, row);
        }
        catch (Exception)
        {
            // Not a real console.
        }

        ConsoleUi.WriteBar("  " + keys, ConsoleUi.Cream, ConsoleColor.DarkBlue);
    }

    // =================================================================
    // Actions
    // =================================================================

    private void PromptForPath()
    {
        ConsoleUi.Clear();
        DrawHeader("Pick ROM file or folder");
        Console.WriteLine();

        string? picked = PromptPath("Path", _state.InputPath);

        if (string.IsNullOrWhiteSpace(picked))
        {
            _screen = Screen.Menu;
            return;
        }

        _state.InputPath = picked;
        ScanCurrentPath();
    }

    private string? PromptPath(string label, string? current)
    {
        ConsoleUi.Reset();
        Console.WriteLine();
        ConsoleUi.WriteLine("  " + label + " (blank keeps the current value)", ConsoleUi.Muted);

        string hint = string.IsNullOrEmpty(current) ? string.Empty : " [" + current + "]";
        ConsoleUi.Write("  >" + hint + " ", ConsoleUi.Gold);

        string? typed = null;

        try
        {
            typed = Console.ReadLine();
        }
        catch (Exception)
        {
            typed = null;
        }

        ConsoleUi.Clear();

        if (string.IsNullOrWhiteSpace(typed))
            return current;

        string value = typed.Trim().Trim('"');
        return value.Length == 0 ? current : value;
    }

    private void ScanCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(_state.InputPath))
        {
            _status = "Pick a path first.";
            _screen = Screen.Menu;
            return;
        }

        ConsoleUi.Clear();
        DrawHeader("Scanning");
        Console.WriteLine();

        _roms = RomScanner.Scan(_state.InputPath, _state.Recursive);

        int done = 0;
        int total = _roms.Count;

        RomScanner.ProbeAll(_roms, (d, t) =>
        {
            done = d;

            if (d % 8 != 0 && d != t)
                return;

            try
            {
                Console.SetCursorPosition(0, 3);
                ConsoleUi.Write("  reading headers " + ConsoleUi.Fit(d + "/" + t, 20), ConsoleUi.Muted);
            }
            catch (Exception)
            {
                // Not a real console.
            }
        });

        _selected.Clear();
        _browserIndex = 0;
        _browserScroll = 0;

        int supported = 0;

        foreach (RomEntry rom in _roms)
        {
            if (rom.MapperSupported)
                supported++;
        }

        _status = _roms.Count == 0
            ? "No .nes files found."
            : $"{_roms.Count} cartridges, {supported} with a supported mapper, {done}/{total} probed.";

        _screen = _roms.Count > 0 ? Screen.Browser : Screen.Menu;
    }

    private List<RomEntry> SelectedEntries()
    {
        List<RomEntry> list = new();

        if (_selected.Count == 0)
        {
            if (_browserIndex >= 0 && _browserIndex < _roms.Count)
                list.Add(_roms[_browserIndex]);

            return list;
        }

        List<int> indices = new(_selected);
        indices.Sort();

        foreach (int index in indices)
        {
            if (index >= 0 && index < _roms.Count)
                list.Add(_roms[index]);
        }

        return list;
    }

    private void StartLift(List<RomEntry> entries)
    {
        if (entries.Count == 0)
            return;

        _log.Clear();
        _liftOk = 0;
        _liftFailed = 0;
        _liftIndex = 0;
        _liftTotal = entries.Count;
        _liftTarget = entries.Count == 1 ? entries[0].FileName : entries.Count + " cartridges";
        _liftClock.Restart();
        _working = true;
        _screen = Screen.Lifting;

        _worker = new Thread(() => LiftWorker(entries))
        {
            IsBackground = true,
            Name = "lift-worker"
        };

        _worker.Start();
    }

    private void LiftWorker(List<RomEntry> entries)
    {
        // Both streams must be captured: the pipeline writes progress to stdout
        // and errors to stderr, and a stray stderr line would land in the middle
        // of the TUI's own drawing.
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(_log);
        Console.SetError(_log);

        try
        {
            foreach (RomEntry rom in entries)
            {
                _liftTarget = rom.FileName;

                try
                {
                    CliOptions options = _state.ToCliOptions(rom.Path);
                    Pipeline pipeline = new Pipeline(options);
                    int code = pipeline.Run();

                    if (code == 0)
                    {
                        _liftOk++;
                        rom.Lifted = true;
                        rom.Result = "ok";
                    }
                    else
                    {
                        _liftFailed++;
                        rom.Result = "failed";
                    }
                }
                catch (Exception ex)
                {
                    _liftFailed++;
                    rom.Result = "failed";
                    _log.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                }

                _liftIndex++;
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            _liftClock.Stop();
            _working = false;
        }
    }

    private void RunSelfTest()
    {
        _log.Clear();
        _liftOk = 0;
        _liftFailed = 0;
        _liftIndex = 0;
        _liftTotal = Common.SyntheticRom.DefaultMatrix.Count;
        _liftTarget = "self-test: " + _liftTotal + " synthetic cartridges";
        _liftClock.Restart();
        _working = true;
        _screen = Screen.Lifting;

        _worker = new Thread(() =>
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            Console.SetOut(_log);
            Console.SetError(_log);

            try
            {
                CliOptions options = _state.ToCliOptions();
                options.RunSelfTest = true;
                options.HeadlessFrames = 30;

                SelfTest test = new SelfTest(options);
                int code = test.Run();

                if (code == 0) _liftOk = _liftTotal;
                else _liftFailed = 1;
            }
            catch (Exception ex)
            {
                _liftFailed = 1;
                _log.WriteLine("ERROR: " + ex.Message);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                _liftClock.Stop();
                _working = false;
                _liftIndex = _liftTotal;
            }
        })
        {
            IsBackground = true,
            Name = "selftest-worker"
        };

        _worker.Start();
    }

    private void OpenOutputFolder()
    {
        try
        {
            string path = Path.GetFullPath(_state.OutputPath);
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _status = "Could not open the folder: " + ex.Message;
        }
    }

    // =================================================================
    // Fallback
    // =================================================================

    /// <summary>
    /// Plain line-driven mode for consoles that cannot do cursor addressing.
    /// Same capabilities, no escape sequences.
    /// </summary>
    private int RunPlainFallback()
    {
        Console.WriteLine();
        Console.WriteLine("NesLifter - static NES recompiler");
        Console.WriteLine("=================================");
        Console.WriteLine();
        Console.WriteLine("No interactive terminal detected, so this is line mode.");
        Console.WriteLine();

        string input = _state.InputPath;

        if (string.IsNullOrWhiteSpace(input))
            input = ConsoleUi.ReadLine("ROM file or folder: ");

        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Nothing to do. Run with --help for the full CLI.");
            return 1;
        }

        _state.InputPath = input.Trim().Trim('"');

        List<RomEntry> roms = RomScanner.Scan(_state.InputPath, _state.Recursive);

        if (roms.Count == 0)
        {
            Console.Error.WriteLine("No .nes files found in " + _state.InputPath);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Found " + roms.Count + " cartridge(s). Reading headers...");
        RomScanner.ProbeAll(roms);

        int supported = 0;

        foreach (RomEntry rom in roms)
        {
            if (rom.MapperSupported)
                supported++;
        }

        Console.WriteLine("  " + roms.Count + " total, " + supported + " with a supported mapper");
        Console.WriteLine();

        int ok = 0;
        int failed = 0;

        foreach (RomEntry rom in roms)
        {
            Console.WriteLine("--- " + rom.FileName + " (mapper " + rom.Mapper + " " + rom.MapperName + ") ---");

            try
            {
                Pipeline pipeline = new Pipeline(_state.ToCliOptions(rom.Path));
                int code = pipeline.Run();

                if (code == 0) ok++;
                else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine("ERROR: " + ex.Message);
            }

            Console.WriteLine();
        }

        Console.WriteLine("Summary: ok=" + ok + ", failed=" + failed);
        return failed == 0 ? 0 : 2;
    }
}
