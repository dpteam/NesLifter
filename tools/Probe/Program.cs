// Headless screenshot and trace harness for lifted games.
//
//   dotnet run --project tools/Probe -- --exe path/to/Game.exe --frames 60,120,300
//
// Runs a lifted game with --headless, asks it to dump PNGs at the given frames,
// optionally captures an APU trace, and then summarises what came back so a
// rendering or audio regression is visible without opening a window.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace NesLifter.Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? exe = null;
        string frames = "60,120,180,300,600";
        string outDir = "probe";
        string? diag = null;
        int budget = 600;
        bool audio = false;
        bool keepLog = false;
        string? press = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--exe" or "-e":
                    if (i + 1 < args.Length) exe = args[++i];
                    break;

                case "--frames" or "-f":
                    if (i + 1 < args.Length) frames = args[++i];
                    break;

                case "--out" or "-o":
                    if (i + 1 < args.Length) outDir = args[++i];
                    break;

                case "--diag" or "-d":
                    if (i + 1 < args.Length) diag = args[++i];
                    break;

                case "--budget" or "-b":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int b)) budget = b;
                    i++;
                    break;

                case "--audio" or "-a":
                    audio = true;
                    break;

                case "--keep-log":
                    keepLog = true;
                    break;

                case "--press" or "-p":
                    if (i + 1 < args.Length) press = args[++i];
                    break;
            }
        }

        if (exe == null || !File.Exists(exe))
        {
            Console.Error.WriteLine("Usage: Probe --exe <Game.exe> [--frames 60,120] [--out dir] [--diag all] [--audio] [--press 200:08]");
            return 1;
        }

        string workDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(workDir);

        int maxFrame = ParseFrames(frames).DefaultIfEmpty(budget).Max();
        if (maxFrame > budget) budget = maxFrame;

        List<string> cli = new()
        {
            "--headless", budget.ToString(CultureInfo.InvariantCulture),
            "--shots", frames,
            "--shot-dir", workDir
        };

        if (diag != null)
        {
            cli.Add("--diag");
            cli.Add(diag);
            cli.Add("--diag-dir");
            cli.Add(workDir);
        }

        if (press != null)
        {
            cli.Add("--press");
            cli.Add(press);
        }

        string? wavPath = null;

        if (audio)
        {
            wavPath = Path.Combine(workDir, "audio.raw");
            if (File.Exists(wavPath)) File.Delete(wavPath);
            cli.Add("--apu-trace");
            cli.Add(wavPath);
        }

        Console.WriteLine("probing " + Path.GetFileName(exe));
        Console.WriteLine("  frames : " + frames);
        Console.WriteLine("  out    : " + workDir);

        ProcessStartInfo psi = new()
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))!
        };

        foreach (string a in cli) psi.ArgumentList.Add(a);

        StringBuilder stdout = new();
        StringBuilder stderr = new();

        using Process process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            try { process.Kill(true); } catch { }
            Console.Error.WriteLine("timed out");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("exit " + process.ExitCode);

        foreach (string line in stdout.ToString().Split('\n'))
        {
            if (line.Trim().Length > 0) Console.WriteLine("  " + line.TrimEnd());
        }

        string err = stderr.ToString().Trim();
        if (err.Length > 0)
        {
            Console.WriteLine("  stderr:");
            foreach (string line in err.Split('\n'))
                if (line.Trim().Length > 0) Console.WriteLine("    " + line.TrimEnd());
        }

        // ---- analyse the frames ----

        List<string> pngs = Directory.GetFiles(workDir, "frame_*.png").OrderBy(p => p).ToList();

        Console.WriteLine();
        Console.WriteLine("frames captured: " + pngs.Count);

        foreach (string png in pngs)
        {
            FrameStats stats = Analyse(png);

            Console.WriteLine(
                "  " + Path.GetFileName(png).PadRight(18) +
                " colors=" + stats.DistinctColors.ToString().PadLeft(3) +
                " ink=" + stats.InkPercent.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + "%" +
                " " + stats.Verdict);
        }

        if (audio && wavPath != null && File.Exists(wavPath))
        {
            AudioStats audioStats = AnalyseAudio(wavPath);
            Console.WriteLine();
            Console.WriteLine("audio: " + audioStats.Describe());
        }

        if (!keepLog)
        {
            // Leave the PNGs, drop the raw traces: they are only useful while debugging.
            foreach (string log in Directory.GetFiles(workDir, "*.log"))
            {
                try { File.Delete(log); } catch { }
            }
        }

        return process.ExitCode;
    }

    private static IEnumerable<int> ParseFrames(string spec)
    {
        foreach (string part in spec.Split(','))
        {
            if (int.TryParse(part.Trim(), out int f) && f >= 0)
                yield return f;
        }
    }

    private sealed record FrameStats(int DistinctColors, double InkPercent, string Verdict);

    /// <summary>
    /// Measures a frame: how many distinct colours it uses and how much of it is
    /// not the backdrop. A correct title screen has many colours and substantial
    /// ink; a broken frame is one flat colour or a repeating pattern.
    /// </summary>
    private static FrameStats Analyse(string path)
    {
        using Bitmap bmp = new(path);

        Dictionary<int, int> histogram = new();
        int total = bmp.Width * bmp.Height;

        BitmapData data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        int backdrop;
        int ink = 0;

        try
        {
            unsafe
            {
                byte* scan = (byte*)data.Scan0;
                backdrop = scan[0] | (scan[1] << 8) | (scan[2] << 16);

                for (int y = 0; y < bmp.Height; y++)
                {
                    byte* row = scan + y * data.Stride;

                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int i = x * 4;
                        int rgb = row[i] | (row[i + 1] << 8) | (row[i + 2] << 16);

                        histogram.TryGetValue(rgb, out int count);
                        histogram[rgb] = count + 1;

                        if (rgb != backdrop) ink++;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        double inkPercent = total == 0 ? 0 : ink * 100.0 / total;
        int distinct = histogram.Count;

        string verdict;

        if (distinct <= 2)
            verdict = "<-- BLANK (single colour)";
        else if (distinct <= 4 && inkPercent < 2)
            verdict = "<-- nearly blank";
        else if (inkPercent < 3)
            verdict = "<-- almost no content";
        else if (distinct > 40)
            verdict = "ok (rich)";
        else
            verdict = "ok";

        return new FrameStats(distinct, inkPercent, verdict);
    }

    private sealed record AudioStats(int Samples, int Silent, int Clipped, double Rms, double Peak)
    {
        public string Describe()
        {
            double silentPercent = Samples == 0 ? 100 : Silent * 100.0 / Samples;

            return $"samples={Samples} silent={silentPercent:0.0}% clipped={Clipped} rms={Rms:0} peak={Peak}";
        }
    }

    /// <summary>
    /// Measures the APU trace, which is signed 16-bit little-endian mono at
    /// 44.1 kHz.
    ///
    /// Silence is the common failure: the mixer runs but every channel is off,
    /// so the file is a run of zeroes. Clipping is the other one — it means the
    /// mixer summed the channels without headroom.
    /// </summary>
    private static AudioStats AnalyseAudio(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        int samples = data.Length / 2;

        if (samples == 0)
            return new AudioStats(0, 0, 0, 0, 0);

        int silent = 0;
        int clipped = 0;
        double sumSquares = 0;
        int peak = 0;

        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            short s = (short)(data[i] | (data[i + 1] << 8));

            if (s == 0) silent++;
            if (s >= 32000 || s <= -32000) clipped++;

            sumSquares += s * (double)s;

            int magnitude = Math.Abs((int)s);
            if (magnitude > peak) peak = magnitude;
        }

        double rms = Math.Sqrt(sumSquares / samples);
        return new AudioStats(samples, silent, clipped, rms, peak);
    }
}
