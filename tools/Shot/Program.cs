// Window screenshot tool for the NesLifter project.
//
// Captures a single window (not the desktop) so screenshots for docs are
// reproducible even on a busy machine or over remote desktop.
//
//   dotnet run --project tools/Shot -- --process NesLifter --out shot.png
//
// Uses PrintWindow with PW_RENDERFULLCONTENT, which asks the window to draw
// itself into a bitmap: the result never includes overlapping windows and does
// not require focus.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NesLifter.Shot;

internal static class Program
{
    private const uint PwRenderFullContent = 0x00000002;

    private static int Main(string[] args)
    {
        string processName = "NesLifter";
        string outPath = "shot.png";
        int delayMs = 800;
        int titleIndex = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--process" or "-p":
                    if (i + 1 < args.Length) processName = args[++i];
                    break;

                case "--out" or "-o":
                    if (i + 1 < args.Length) outPath = args[++i];
                    break;

                case "--delay" or "-d":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed)) delayMs = parsed;
                    i++;
                    break;

                case "--index":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int index)) titleIndex = index;
                    i++;
                    break;
            }
        }

        if (delayMs > 0)
            Thread.Sleep(delayMs);

        Process[] candidates = Process.GetProcessesByName(processName);

        if (candidates.Length == 0)
        {
            Console.Error.WriteLine("No process named '" + processName + "' is running.");
            return 1;
        }

        List<(IntPtr Handle, string Title)> windows = new();

        foreach (Process process in candidates)
        {
            CollectWindows(process.Id, windows);
        }

        if (windows.Count == 0)
        {
            Console.Error.WriteLine("No visible window found for '" + processName + "'.");
            return 2;
        }

        if (titleIndex < 0 || titleIndex >= windows.Count)
            titleIndex = 0;

        (IntPtr handle, string title) = windows[titleIndex];

        Console.WriteLine("window: " + title);

        if (!Capture(handle, outPath, out int width, out int height))
        {
            Console.Error.WriteLine("PrintWindow failed for handle " + handle + ".");
            return 3;
        }

        Console.WriteLine($"captured {width}x{height} -> {outPath}");
        return 0;
    }

    /// <summary>Enumerates top-level, visible, reasonably sized windows of a process.</summary>
    private static void CollectWindows(int processId, List<(IntPtr, string)> output)
    {
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);

            if (pid != (uint)processId)
                return true;

            if (!IsWindowVisible(hwnd))
                return true;

            if (!GetWindowRect(hwnd, out Rect rect))
                return true;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            if (w < 200 || h < 150)
                return true;

            int length = GetWindowTextLength(hwnd);
            string title = string.Empty;

            if (length > 0)
            {
                System.Text.StringBuilder sb = new(length + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            output.Add((hwnd, title.Length > 0 ? title : "(untitled)"));
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Renders the window into a bitmap and saves it as PNG.
    /// </summary>
    private static bool Capture(IntPtr hwnd, string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!GetWindowRect(hwnd, out Rect rect))
            return false;

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return false;

        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();

            try
            {
                if (!PrintWindow(hwnd, hdc, PwRenderFullContent))
                    return false;
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);
}
