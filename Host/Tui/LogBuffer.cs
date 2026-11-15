using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using NesLifter.Compilation;

namespace NesLifter.Host.Tui;

/// <summary>
/// Captures console output into a bounded ring buffer so the TUI can show a
/// live log tail while a lift runs on a background thread.
/// </summary>
public sealed class LogBuffer : TextWriter
{
    private readonly object _sync = new();
    private readonly Queue<string> _lines = new();
    private readonly int _capacity;

    public LogBuffer(int capacity = 500)
    {
        _capacity = capacity;
    }

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Total number of lines ever written, including evicted ones.</summary>
    public long TotalLines { get; private set; }

    public override void Write(char value)
    {
        Write(value.ToString());
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        lock (_sync)
        {
            foreach (string raw in value.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                if (line.Length == 0 && _lines.Count > 0)
                    continue;

                _lines.Enqueue(line);
                TotalLines++;

                while (_lines.Count > _capacity)
                    _lines.Dequeue();
            }
        }
    }

    public override void WriteLine(string? value)
    {
        Write((value ?? string.Empty) + "\n");
    }

    /// <summary>Returns the last <paramref name="count"/> lines.</summary>
    public List<string> Tail(int count)
    {
        lock (_sync)
        {
            List<string> all = new List<string>(_lines);

            if (all.Count <= count)
                return all;

            return all.GetRange(all.Count - count, count);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _lines.Clear();
            TotalLines = 0;
        }
    }
}

/// <summary>Options the TUI edits, mapped 1:1 onto <see cref="CliOptions"/>.</summary>
public sealed class TuiState
{
    public string InputPath = string.Empty;
    public string OutputPath = "nes_lifted_output";
    public bool Recursive = true;
    public bool SaveSource = true;
    public PackageMode PackageMode = PackageMode.AppHost;
    public int CaptureIconSeconds = 5;
    public string? IconPath;

    /// <summary>UI theme: light, dark or oled.</summary>
    public string Theme = "dark";

    /// <summary>Interface sounds on/off.</summary>
    public bool Sound;

    public CliOptions ToCliOptions(string? inputOverride = null)
    {
        return new CliOptions
        {
            InputPath = inputOverride ?? InputPath,
            OutputPath = OutputPath,
            Recursive = Recursive,
            SaveSource = SaveSource,
            NoCompile = false,
            PackageMode = PackageMode,
            CaptureIconSeconds = CaptureIconSeconds,
            IconPath = IconPath
        };
    }
}
