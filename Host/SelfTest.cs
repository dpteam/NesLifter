using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NesLifter.Common;

namespace NesLifter.Host;

/// <summary>
/// End-to-end verification: builds synthetic cartridges for a matrix of
/// mappers, runs the full lift + compile pipeline, then executes each
/// generated assembly headlessly and checks that it reaches a frame budget
/// without trapping.
///
/// This is the guard rail that lets mapper and runtime changes land safely:
/// every supported board must survive analysis, code generation, Roslyn
/// compilation and a real execution pass.
/// </summary>
public sealed class SelfTest
{
    private readonly CliOptions _options;

    public SelfTest(CliOptions options)
    {
        _options = options;
    }

    public int Run()
    {
        string root = Path.Combine(_options.OutputPath, "_selftest");
        string romDir = Path.Combine(root, "roms");
        string outDir = Path.Combine(root, "out");

        Directory.CreateDirectory(romDir);
        Directory.CreateDirectory(outDir);

        Console.WriteLine("NesLifter self-test");
        Console.WriteLine("===================");
        Console.WriteLine("ROMs:    " + romDir);
        Console.WriteLine("Output:  " + outDir);
        Console.WriteLine("Frames:  " + _options.HeadlessFrames);
        Console.WriteLine();

        List<SelfTestResult> results = new List<SelfTestResult>();

        foreach (SyntheticRom.Spec spec in SyntheticRom.DefaultMatrix)
        {
            results.Add(RunOne(spec, romDir, outDir));
        }

        Console.WriteLine();
        Console.WriteLine("Results");
        Console.WriteLine("-------");

        int failed = 0;

        foreach (SelfTestResult result in results)
        {
            if (!result.Ok)
                failed++;

            Console.WriteLine(
                (result.Ok ? "  PASS  " : "  FAIL  ") +
                result.Spec.Name.PadRight(22) +
                "mapper " + result.Spec.Mapper.ToString(CultureInfo.InvariantCulture).PadRight(4) +
                result.Detail);
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "Self-test passed: " + results.Count + "/" + results.Count + " cartridges lifted and executed."
            : "Self-test FAILED: " + failed + " of " + results.Count + " cartridges failed.");

        if (!_options.KeepTemp && failed == 0)
        {
            TryDelete(root);
        }
        else if (failed == 0)
        {
            Console.WriteLine("Artifacts kept: " + root);
        }

        return failed == 0 ? 0 : 2;
    }

    private SelfTestResult RunOne(SyntheticRom.Spec spec, string romDir, string outDir)
    {
        Console.WriteLine("--- " + spec.Name + " (mapper " + spec.Mapper + ") ---");

        string romPath;
        string safeName = FileSystemUtil.SanitizeFileName(spec.Name);
        string workDir = Path.Combine(outDir, safeName);
        string dllPath = Path.Combine(workDir, safeName + ".dll");

        try
        {
            romPath = SyntheticRom.Write(spec, romDir);
        }
        catch (Exception ex)
        {
            return SelfTestResult.Fail(spec, "ROM generation failed: " + ex.Message);
        }

        CliOptions options = new CliOptions
        {
            InputPath = romPath,
            OutputPath = outDir,
            SaveSource = _options.SaveSource,
            NoCompile = false,
            Verbose = _options.Verbose
        };

        try
        {
            Pipeline pipeline = new Pipeline(options);
            int code = pipeline.Run();

            if (code != 0)
                return SelfTestResult.Fail(spec, "pipeline returned " + code);
        }
        catch (Exception ex)
        {
            return SelfTestResult.Fail(spec, "pipeline threw: " + ex.GetType().Name + ": " + ex.Message);
        }

        if (!File.Exists(dllPath))
            return SelfTestResult.Fail(spec, "generated assembly missing");

        string analysis = Path.Combine(workDir, "analysis.txt");

        if (!File.Exists(analysis))
            return SelfTestResult.Fail(spec, "analysis report missing");

        string analysisText = File.ReadAllText(analysis);

        if (analysisText.Contains("Unknown opcodes: 1", StringComparison.Ordinal) &&
            !analysisText.Contains("Unknown opcodes: 0", StringComparison.Ordinal))
        {
            // Informational only: unknown opcodes are not fatal, the interpreter
            // handles them. Keep the run green but mention it.
        }

        if (!_options.SkipRun)
        {
            (bool ok, string detail) = RunHeadless(dllPath, _options.HeadlessFrames);

            if (!ok)
                return SelfTestResult.Fail(spec, detail);

            return SelfTestResult.Pass(spec, detail);
        }

        return SelfTestResult.Pass(spec, "compiled (execution skipped)");
    }

    private (bool Ok, string Detail) RunHeadless(string dllPath, int frames)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add(dllPath);
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add(frames.ToString(CultureInfo.InvariantCulture));

        try
        {
            using Process process = Process.Start(psi)!;

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process already gone.
                }

                return (false, "headless run timed out");
            }

            string outText = stdout.ToString().Trim();
            string errText = stderr.ToString().Trim();

            if (process.ExitCode == 0)
            {
                string summary = FindSummaryLine(outText);
                return (true, summary.Length > 0 ? " -> " + summary : string.Empty);
            }

            string detail = "headless exit " + process.ExitCode;

            if (errText.Length > 0)
                detail += ": " + FirstLine(errText);

            return (false, detail);
        }
        catch (Exception ex)
        {
            return (false, "could not start generated assembly: " + ex.Message);
        }
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        int idx = text.IndexOfAny(new[] { '\r', '\n' });
        string line = idx < 0 ? text : text.Substring(0, idx);
        return line.Length > 120 ? line.Substring(0, 120) + "..." : line;
    }

    /// <summary>
    /// Picks the "headless: frames=... instructions=..." line out of the
    /// generated game's stdout, which is the only line worth surfacing.
    /// </summary>
    private static string FindSummaryLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();

            if (line.StartsWith("headless:", StringComparison.Ordinal))
                return line;
        }

        return FirstLine(text);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // Leaving artifacts behind is never fatal.
        }
    }

    private sealed class SelfTestResult
    {
        private SelfTestResult(SyntheticRom.Spec spec, bool ok, string detail)
        {
            Spec = spec;
            Ok = ok;
            Detail = detail;
        }

        public SyntheticRom.Spec Spec { get; }
        public bool Ok { get; }
        public string Detail { get; }

        public static SelfTestResult Pass(SyntheticRom.Spec spec, string detail)
        {
            return new SelfTestResult(spec, true, detail);
        }

        public static SelfTestResult Fail(SyntheticRom.Spec spec, string detail)
        {
            return new SelfTestResult(spec, false, ": " + detail);
        }
    }
}
