using System;
using System.Collections.Generic;
using System.IO;

namespace NesLifter.Host;

public sealed class CliOptions
{
    public string InputPath = string.Empty;
    public string OutputPath = string.Empty;

    public bool Recursive;
    public bool SaveSource = true;
    public bool NoCompile;
    public bool ShowHelp;

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

            if (Is(a, "-r", "--recursive"))
            {
                o.Recursive = true;
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
                // Оставляем как есть.
            }
        }

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