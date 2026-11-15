using System;
using System.Collections.Generic;
using System.IO;
using NesLifter.Compilation;

namespace NesLifter.Host;

public sealed class CliOptions
{
    public string InputPath = string.Empty;
    public string OutputPath = string.Empty;

    public bool Recursive;
    public bool SaveSource = true;
    public bool NoCompile;
    public bool ShowHelp;
    public bool ShowVersion;
    public bool Verbose;
    public bool RunSelfTest;
    public bool SkipRun;
    public bool KeepTemp;
    public int HeadlessFrames = 30;

    /// <summary>How to deliver the generated game.</summary>
    public PackageMode PackageMode = PackageMode.AppHost;

    /// <summary>Custom icon for the generated executable, or null.</summary>
    public string? IconPath;

    /// <summary>Seconds before the generated game captures its own icon.</summary>
    public int CaptureIconSeconds = 5;

    /// <summary>Skip the interactive UI even when launched without arguments.</summary>
    public bool NoTui;

    /// <summary>Launch the WebView2 GUI.</summary>
    public bool Gui;

    /// <summary>
    /// True when the user explicitly passed -r/--recursive. The interactive UIs
    /// default to recursive scanning because a ROM library is always nested,
    /// but an explicit flag must still win.
    /// </summary>
    public bool RecursiveSpecified;

    public List<string> UnknownArgs = new List<string>();

    public static CliOptions Parse(string[] args)
    {
        CliOptions o = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (string.IsNullOrWhiteSpace(a))
                continue;

            if (Is(a, "-h", "--help", "/?"))
            {
                o.ShowHelp = true;
                continue;
            }

            if (Is(a, "--version"))
            {
                o.ShowVersion = true;
                continue;
            }

            if (Is(a, "-r", "--recursive"))
            {
                o.Recursive = true;
                o.RecursiveSpecified = true;
                continue;
            }

            if (Is(a, "-v", "--verbose"))
            {
                o.Verbose = true;
                continue;
            }

            if (Is(a, "--no-compile"))
            {
                o.NoCompile = true;
                continue;
            }

            if (Is(a, "--no-source"))
            {
                o.SaveSource = false;
                continue;
            }

            if (Is(a, "--keep-source"))
            {
                o.SaveSource = true;
                continue;
            }

            if (Is(a, "--selftest"))
            {
                o.RunSelfTest = true;
                continue;
            }

            if (Is(a, "--no-run"))
            {
                o.SkipRun = true;
                continue;
            }

            if (Is(a, "--keep"))
            {
                o.KeepTemp = true;
                continue;
            }

            if (Is(a, "--dll"))
            {
                o.PackageMode = PackageMode.AssemblyOnly;
                continue;
            }

            if (Is(a, "--exe"))
            {
                o.PackageMode = PackageMode.AppHost;
                continue;
            }

            if (Is(a, "--full-exe", "--self-contained", "--standalone"))
            {
                o.PackageMode = PackageMode.SelfContained;
                continue;
            }

            if (Is(a, "--icon"))
            {
                if (i + 1 < args.Length)
                    o.IconPath = args[++i];

                continue;
            }

            if (Is(a, "--capture-icon"))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int iconSeconds))
                {
                    o.CaptureIconSeconds = iconSeconds;
                    i++;
                }

                continue;
            }

            if (Is(a, "--no-icon"))
            {
                o.CaptureIconSeconds = 0;
                continue;
            }

            if (Is(a, "--no-tui", "--cli"))
            {
                o.NoTui = true;
                continue;
            }

            if (Is(a, "--gui", "--ui"))
            {
                o.Gui = true;
                continue;
            }

            if (Is(a, "-i", "--input"))
            {
                if (i + 1 < args.Length)
                    o.InputPath = args[++i];

                continue;
            }

            if (Is(a, "-o", "--output"))
            {
                if (i + 1 < args.Length)
                    o.OutputPath = args[++i];

                continue;
            }

            if (Is(a, "--frames"))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int frames))
                {
                    o.HeadlessFrames = frames;
                    i++;
                }

                continue;
            }

            if (TryGetValue(a, "--input", out string input))
            {
                o.InputPath = input;
                continue;
            }

            if (TryGetValue(a, "--output", out string output))
            {
                o.OutputPath = output;
                continue;
            }

            if (TryGetValue(a, "--frames", out string framesValue) &&
                int.TryParse(framesValue, out int parsedFrames))
            {
                o.HeadlessFrames = parsedFrames;
                continue;
            }

            if (TryGetValue(a, "--icon", out string iconValue))
            {
                o.IconPath = iconValue;
                continue;
            }

            if (TryGetValue(a, "--capture-icon", out string captureValue) &&
                int.TryParse(captureValue, out int parsedCapture))
            {
                o.CaptureIconSeconds = parsedCapture;
                continue;
            }

            if (!a.StartsWith('-') && string.IsNullOrWhiteSpace(o.InputPath))
            {
                o.InputPath = a;
                continue;
            }

            o.UnknownArgs.Add(a);
        }

        if (string.IsNullOrWhiteSpace(o.OutputPath))
            o.OutputPath = "nes_lifted_output";

        try
        {
            o.OutputPath = Path.GetFullPath(o.OutputPath);
        }
        catch
        {
            o.OutputPath = Path.Combine(Environment.CurrentDirectory, "nes_lifted_output");
        }

        if (!string.IsNullOrWhiteSpace(o.InputPath))
        {
            try
            {
                o.InputPath = Path.GetFullPath(o.InputPath);
            }
            catch
            {
                // Keep the raw value; the pipeline reports it as missing.
            }
        }

        if (o.HeadlessFrames <= 0)
            o.HeadlessFrames = 30;

        return o;
    }

    private static bool Is(string arg, params string[] names)
    {
        foreach (string name in names)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryGetValue(string arg, string prefix, out string value)
    {
        string fullPrefix = prefix + "=";

        if (arg.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg.Substring(fullPrefix.Length);
            return true;
        }

        value = string.Empty;
        return false;
    }
}
