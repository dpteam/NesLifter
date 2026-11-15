using System;
using System.Text;

namespace NesLifter.Host.Tui;

/// <summary>
/// Console drawing helpers.
///
/// Everything degrades to plain text when the console cannot do cursor
/// addressing or colour, so the TUI never emits escape garbage into a pipe or
/// a dumb terminal.
/// </summary>
public static class ConsoleUi
{
    private static bool? _interactive;

    /// <summary>True when the console can host an interactive full-screen UI.</summary>
    public static bool IsInteractive
    {
        get
        {
            if (_interactive.HasValue)
                return _interactive.Value;

            _interactive = Probe();
            return _interactive.Value;
        }
    }

    private static bool Probe()
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
                return false;

            if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(Environment.GetEnvironmentVariable("NesLifter_NO_TUI"), "1", StringComparison.Ordinal))
                return false;

            return Console.WindowWidth > 40 && Console.WindowHeight > 12;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SupportsColor
    {
        get
        {
            try
            {
                return !Console.IsOutputRedirected &&
                       !string.Equals(Environment.GetEnvironmentVariable("NO_COLOR"), "1", StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static int Width
    {
        get
        {
            try { return Math.Max(50, Console.WindowWidth); }
            catch (Exception) { return 90; }
        }
    }

    public static int Height
    {
        get
        {
            try { return Math.Max(14, Console.WindowHeight); }
            catch (Exception) { return 30; }
        }
    }

    // The GUI's palette, mapped onto the 16 console colours.
    public const ConsoleColor Ink = ConsoleColor.Gray;
    public const ConsoleColor Cream = ConsoleColor.White;
    public const ConsoleColor Red = ConsoleColor.Red;
    public const ConsoleColor Gold = ConsoleColor.Yellow;
    public const ConsoleColor Muted = ConsoleColor.DarkGray;
    public const ConsoleColor Accent = ConsoleColor.Cyan;
    public const ConsoleColor Good = ConsoleColor.Green;

    public static void Clear()
    {
        try
        {
            Console.Clear();
            Console.CursorVisible = false;
        }
        catch (Exception)
        {
            // Not a real console.
        }
    }

    public static void Reset()
    {
        try
        {
            Console.ResetColor();
            Console.CursorVisible = true;
        }
        catch (Exception)
        {
            // Ignore.
        }
    }

    public static void Write(string text, ConsoleColor color)
    {
        if (!SupportsColor)
        {
            Console.Write(text);
            return;
        }

        ConsoleColor previous = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    public static void WriteLine()
    {
        Console.WriteLine();
    }

    /// <summary>Full-width coloured bar, used for the header and footer.</summary>
    public static void WriteBar(string text, ConsoleColor foreground, ConsoleColor background)
    {
        string padded = Fit(text, Width);

        if (!SupportsColor)
        {
            Console.WriteLine(padded);
            return;
        }

        ConsoleColor fg = Console.ForegroundColor;
        ConsoleColor bg = Console.BackgroundColor;

        try
        {
            Console.ForegroundColor = foreground;
            Console.BackgroundColor = background;
            Console.Write(padded);
            Console.ResetColor();
        }
        finally
        {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg;
        }

        Console.WriteLine();
    }

    /// <summary>Truncates or pads to exactly <paramref name="width"/> columns.</summary>
    public static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        text ??= string.Empty;

        if (text.Length == width)
            return text;

        if (text.Length > width)
            return width <= 1 ? text.Substring(0, 1) : text.Substring(0, width - 1) + "\u2026";

        return text.PadRight(width);
    }

    public static string PadLeft(string text, int width)
    {
        text ??= string.Empty;

        if (text.Length >= width)
            return text.Substring(text.Length - width);

        return text.PadLeft(width);
    }

    public static void Rule(char left, char fill, char right, ConsoleColor color)
    {
        int width = Width;

        if (width < 3)
        {
            Console.WriteLine();
            return;
        }

        StringBuilder sb = new StringBuilder(width);
        sb.Append(left);

        for (int i = 1; i < width - 1; i++)
            sb.Append(fill);

        sb.Append(right);

        WriteLine(sb.ToString(), color);
    }

    /// <summary>Reads one key without echo, or null when no console is available.</summary>
    public static ConsoleKeyInfo? ReadKey()
    {
        try
        {
            if (!IsInteractive)
                return null;

            return Console.ReadKey(intercept: true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string ReadLine(string prompt)
    {
        Write(prompt, Gold);

        try
        {
            return Console.ReadLine() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Positions the cursor, ignoring failures on odd consoles.</summary>
    public static void MoveTo(int left, int top)
    {
        try
        {
            Console.SetCursorPosition(Math.Max(0, left), Math.Max(0, top));
        }
        catch (Exception)
        {
            // Not addressable; the caller's layout degrades to sequential output.
        }
    }
}
