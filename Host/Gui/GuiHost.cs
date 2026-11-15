using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NesLifter.Compilation;
using NesLifter.Host.Tui;

namespace NesLifter.Host.Gui;

/// <summary>
/// WinForms + WebView2 front end.
///
/// The UI itself is plain HTML/CSS/JS in <c>web/</c>, served from a virtual
/// host so it loads instantly and works offline. This class owns the window,
/// the WebView, and the bridge: it receives JSON messages from the page, runs
/// the pipeline on a worker thread, and streams log lines back.
/// </summary>
public sealed class GuiHost : Form
{
    private const string VirtualHost = "neslifter.local";

    private readonly TuiState _state;
    private readonly WebView2 _webView;
    private readonly SynchronizationContext _ui;

    private List<RomEntry> _roms = new();
    private volatile bool _busy;
    private CancellationTokenSource? _cancel;

    /// <summary>
    /// Set when the WebView2 runtime could not be initialised. The caller reads
    /// this after the form closes and falls back to the terminal UI instead of
    /// leaving the user with a dead window.
    /// </summary>
    public string? StartupError => _startupError;

    private string? _startupError;

    public GuiHost(TuiState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        Text = "NesLifter";
        ClientSize = new System.Drawing.Size(1180, 760);
        MinimumSize = new System.Drawing.Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception)
        {
            // No icon is not worth failing over.
        }

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(18, 22, 31)
        };

        Controls.Add(_webView);

        Load += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => _cancel?.Cancel();
    }

    private async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NesLifter",
                "WebView2");

            Directory.CreateDirectory(userData);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userData,
                options: BuildEnvironmentOptions());

            await _webView.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = _webView.CoreWebView2;

            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Serve web/ from disk through a virtual host: no file:// quirks,
            // no local HTTP server, and vendor assets resolve as normal URLs.
            string webRoot = ResolveWebRoot();

            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessage;

            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    return;

                SendInit();

                // A path given on the command line means the user asked for that
                // folder, so scan it immediately instead of waiting for a click.
                if (!string.IsNullOrWhiteSpace(_state.InputPath))
                    ScanAsync(_state.InputPath, _state.Recursive);
            };

            core.Navigate($"https://{VirtualHost}/index.html");
        }
        catch (Exception ex)
        {
            _startupError = ex.Message;
            Close();
        }
    }

    /// <summary>
    /// Chromium environment options tuned for remote desktop and virtual
    /// machines.
    ///
    /// Over RDP there is no usable GPU for compositing, and Chromium's GPU path
    /// either fails outright or renders a blank surface. Forcing software
    /// rendering costs a little CPU and makes the window work everywhere,
    /// including inside a VM, over RDP, and on a headless server with a virtual
    /// display.
    /// </summary>
    private static CoreWebView2EnvironmentOptions BuildEnvironmentOptions()
    {
        CoreWebView2EnvironmentOptions options = new()
        {
            AdditionalBrowserArguments =
                "--disable-gpu " +
                "--disable-gpu-compositing " +
                "--force-color-profile=srgb " +
                "--disable-background-timer-throttling"
        };

        return options;
    }

    /// <summary>
    /// Finds the folder holding the UI. Works both from a build output (web/ is
    /// copied next to the exe) and from a published single file.
    /// </summary>
    private static string ResolveWebRoot()
    {
        string baseDir = AppContext.BaseDirectory;

        string[] candidates =
        {
            Path.Combine(baseDir, "web"),
            Path.Combine(baseDir, "..", "..", "..", "web"),
            Path.Combine(Directory.GetCurrentDirectory(), "web")
        };

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);

            if (File.Exists(Path.Combine(full, "index.html")))
                return full;
        }

        throw new DirectoryNotFoundException(
            "Could not find the web/ folder next to the executable.");
    }

    // =================================================================
    // Bridge
    // =================================================================

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;

        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (Exception)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
            return;

        GuiMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<GuiMessage>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Send("error", new { text = "Bad message: " + ex.Message });
            return;
        }

        if (message == null || string.IsNullOrEmpty(message.Type))
            return;

        switch (message.Type)
        {
            case "ready":
                SendInit();
                break;

            case "scan":
                ScanAsync(message.Path, message.Recursive);
                break;

            case "lift":
                LiftAsync(message);
                break;

            case "selfTest":
                SelfTestAsync();
                break;

            case "openOutput":
                OpenFolder(message.Path);
                break;

            case "pickIcon":
                PickIcon();
                break;

            case "pickFolder":
                PickFolder();
                break;

            case "saveTheme":
                _state.Theme = message.Theme ?? "dark";
                break;

            case "saveSound":
                _state.Sound = message.Enabled;
                break;
        }
    }

    private void SendInit()
    {
        Send("init", new
        {
            theme = _state.Theme,
            sound = _state.Sound,
            outputPath = _state.OutputPath,
            packageMode = _state.PackageMode.ToString(),
            iconSeconds = _state.CaptureIconSeconds,
            path = _state.InputPath,
            recursive = _state.Recursive,
            saveSource = _state.SaveSource
        });
    }

    private void Send(string type, object payload)
    {
        if (IsDisposed || !_webView.IsHandleCreated)
            return;

        void SendCore()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    Merge(type, payload),
                    JsonOptions);

                _webView.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception)
            {
                // The window is closing; nothing to report to.
            }
        }

        if (InvokeRequired)
            BeginInvoke(new Action(SendCore));
        else
            SendCore();
    }

    private static Dictionary<string, object?> Merge(string type, object payload)
    {
        Dictionary<string, object?> map = new() { ["type"] = type };

        foreach (JsonProperty property in JsonSerializer.SerializeToElement(payload, JsonOptions).EnumerateObject())
            map[property.Name] = property.Value;

        return map;
    }

    // =================================================================
    // Actions
    // =================================================================

    private void ScanAsync(string? path, bool recursive)
    {
        if (_busy)
            return;

        if (string.IsNullOrWhiteSpace(path))
        {
            Send("error", new { text = "Enter a path first." });
            return;
        }

        _state.InputPath = path!;
        _state.Recursive = recursive;

        Task.Run(() =>
        {
            try
            {
                List<RomEntry> roms = RomScanner.Scan(path!, recursive);

                foreach (RomEntry rom in roms)
                    rom.Probe();

                _roms = roms;

                List<object> payload = new(roms.Count);

                for (int i = 0; i < roms.Count; i++)
                {
                    RomEntry rom = roms[i];

                    payload.Add(new
                    {
                        index = i,
                        fileName = rom.FileName,
                        mapper = rom.Mapper,
                        mapperName = rom.MapperName,
                        mapperSupported = rom.MapperSupported,
                        sizeLabel = rom.SizeLabel()
                    });
                }

                Send("roms", new { roms = payload });
            }
            catch (Exception ex)
            {
                Send("error", new { text = "Scan failed: " + ex.Message });
            }
        });
    }

    private void LiftAsync(GuiMessage message)
    {
        if (_busy)
            return;

        int[] indices = message.Indices ?? Array.Empty<int>();

        if (indices.Length == 0)
        {
            Send("error", new { text = "Nothing selected." });
            return;
        }

        _busy = true;
        _cancel = new CancellationTokenSource();

        // Apply the options the page sent.
        if (!string.IsNullOrWhiteSpace(message.OutputPath))
            _state.OutputPath = message.OutputPath!;

        _state.PackageMode = ParseMode(message.PackageMode);
        _state.CaptureIconSeconds = message.IconSeconds;
        _state.SaveSource = message.SaveSource;
        _state.IconPath = string.IsNullOrWhiteSpace(message.IconPath) ? null : message.IconPath;

        Task.Run(() =>
        {
            int ok = 0;
            int failed = 0;

            try
            {
                for (int n = 0; n < indices.Length; n++)
                {
                    if (_cancel?.IsCancellationRequested == true)
                        break;

                    int index = indices[n];

                    if (index < 0 || index >= _roms.Count)
                        continue;

                    RomEntry rom = _roms[index];

                    Send("progress", new { done = n, total = indices.Length });
                    Send("log", new { text = "=== " + rom.FileName + " ===" });

                    bool success;

                    try
                    {
                        success = LiftOne(rom);
                    }
                    catch (Exception ex)
                    {
                        Send("log", new { text = "ERROR: " + ex.Message });
                        success = false;
                    }

                    if (success)
                        ok++;
                    else
                        failed++;

                    Send("romResult", new { index = index, ok = success });
                    Send("progress", new { done = n + 1, total = indices.Length });
                }
            }
            finally
            {
                _busy = false;

                Send("done", new
                {
                    ok = ok,
                    failed = failed,
                    summary = failed == 0
                        ? ok + " lifted"
                        : ok + " lifted, " + failed + " failed"
                });
            }
        });
    }

    /// <summary>
    /// Lifts one cartridge, streaming its console output to the UI.
    /// </summary>
    private bool LiftOne(RomEntry rom)
    {
        CliOptions options = _state.ToCliOptions(rom.Path);

        // The pipeline writes progress to stdout and diagnostics to stderr;
        // both have to be forwarded, or half the log never reaches the page.
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        LineForwardingWriter forwarder = new(line => Send("log", new { text = line }));

        Console.SetOut(forwarder);
        Console.SetError(forwarder);

        try
        {
            Pipeline pipeline = new Pipeline(options);
            return pipeline.Run() == 0;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private void SelfTestAsync()
    {
        if (_busy)
            return;

        _busy = true;

        Task.Run(() =>
        {
            int ok = 0;
            int failed = 0;

            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            LineForwardingWriter forwarder = new(line => Send("log", new { text = line }));

            Console.SetOut(forwarder);
            Console.SetError(forwarder);

            try
            {
                CliOptions options = _state.ToCliOptions();
                options.RunSelfTest = true;
                options.HeadlessFrames = 30;

                int code = new SelfTest(options).Run();

                if (code == 0) ok = 1;
                else failed = 1;
            }
            catch (Exception ex)
            {
                Send("log", new { text = "ERROR: " + ex.Message });
                failed = 1;
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                _busy = false;
            }

            Send("done", new
            {
                ok = ok,
                failed = failed,
                summary = failed == 0 ? "Self-test passed" : "Self-test failed"
            });
        });
    }

    private void OpenFolder(string? path)
    {
        try
        {
            string target = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? _state.OutputPath : path!);
            Directory.CreateDirectory(target);

            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Send("error", new { text = "Could not open the folder: " + ex.Message });
        }
    }

    private void PickIcon()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Choose an icon image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            Send("iconPicked", new { path = dialog.FileName });
    }

    private void PickFolder()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose a folder with .nes files",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            Send("folderPicked", new { path = dialog.SelectedPath });
    }

    private static PackageMode ParseMode(string? text)
    {
        return text switch
        {
            "AssemblyOnly" => PackageMode.AssemblyOnly,
            "SelfContained" => PackageMode.SelfContained,
            _ => PackageMode.AppHost
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>One message from the page.</summary>
public sealed class GuiMessage
{
    public string? Type { get; set; }
    public string? Path { get; set; }
    public bool Recursive { get; set; }
    public int[]? Indices { get; set; }
    public string? PackageMode { get; set; }
    public int IconSeconds { get; set; }
    public string? IconPath { get; set; }
    public string? OutputPath { get; set; }
    public bool SaveSource { get; set; }
    public string? Theme { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>
/// Forwards <see cref="Console"/> output line by line, so pipeline progress can
/// be streamed to a UI instead of a terminal.
/// </summary>
public sealed class LineForwardingWriter : TextWriter
{
    private readonly Action<string> _onLine;
    private readonly System.Text.StringBuilder _buffer = new();

    public LineForwardingWriter(Action<string> onLine)
    {
        _onLine = onLine;
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            Flush();
            return;
        }

        if (value != '\r')
            _buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (char c in value)
            Write(c);
    }

    public override void Flush()
    {
        if (_buffer.Length == 0)
            return;

        string line = _buffer.ToString();
        _buffer.Clear();

        if (line.Length > 0)
            _onLine(line);
    }
}
