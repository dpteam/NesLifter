using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NesLifter.Compilation;

/// <summary>How the lifted game should be delivered.</summary>
public enum PackageMode
{
    /// <summary>Only <c>Game.dll</c>, run with <c>dotnet Game.dll</c>.</summary>
    AssemblyOnly,

    /// <summary>
    /// <c>Game.exe</c> + <c>Game.dll</c> + deps/runtimeconfig, the shape every
    /// .NET 5+ desktop app ships in. Needs the .NET desktop runtime installed.
    /// </summary>
    AppHost,

    /// <summary>
    /// Self-contained single-file executable: runs on a machine with no .NET
    /// installed at all. Costs tens of megabytes per game.
    /// </summary>
    SelfContained
}

/// <summary>Options for <see cref="GamePackager"/>.</summary>
public sealed class PackageOptions
{
    public PackageMode Mode { get; set; } = PackageMode.AppHost;

    /// <summary>Absolute path of an <c>.ico</c> to embed, or null for the default.</summary>
    public string? IconPath { get; set; }

    /// <summary>Assembly name without extension, e.g. <c>Super Mario Bros. (W) [!]</c>.</summary>
    public string AssemblyName { get; set; } = "Game";

    /// <summary>Target runtime identifier for self-contained builds.</summary>
    public string RuntimeIdentifier { get; set; } = "win-x64";

    public bool Verbose { get; set; }
}

/// <summary>Result of packaging a generated game.</summary>
public sealed class PackageResult
{
    public bool Success { get; init; }

    /// <summary>Path of the file the user should launch.</summary>
    public string LaunchPath { get; init; } = string.Empty;

    /// <summary>Everything that was produced, newest first is not guaranteed.</summary>
    public IReadOnlyList<string> Produced { get; init; } = Array.Empty<string>();

    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Turns the generated C# into something double-clickable.
///
/// Two strategies, chosen by <see cref="PackageOptions.Mode"/>:
///
/// * <b>AppHost</b> — <c>dotnet publish</c> of a throwaway project produces a
///   real <c>Game.exe</c> launcher plus the managed <c>Game.dll</c>, a
///   <c>.deps.json</c> and a <c>.runtimeconfig.json</c>. The launcher is a
///   normal .NET apphost, so it also lets the SDK embed the application icon
///   into the PE resource table, which is the only way Explorer will show it.
///
/// * <b>SelfContained</b> — the same, with the runtime bundled into one file.
///   Tens of megabytes, but it runs on a machine with no .NET at all.
///
/// When the SDK is unavailable the packager reports failure instead of
/// half-writing files, and the caller falls back to the Roslyn-only assembly.
/// </summary>
public sealed class GamePackager
{
    /// <summary>
    /// Packages <paramref name="sourceCode"/> into <paramref name="workDir"/>.
    /// </summary>
    public PackageResult Package(string sourceCode, string workDir, PackageOptions options, Action<string> log)
    {
        if (sourceCode == null)
            throw new ArgumentNullException(nameof(sourceCode));

        if (string.IsNullOrWhiteSpace(workDir))
            throw new ArgumentException("Work directory is required.", nameof(workDir));

        string safeName = SanitizeAssemblyName(options.AssemblyName);
        string projectDir = Path.Combine(workDir, "_pkg");
        string? publishDir = null;

        try
        {
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(projectDir);

            string sourcePath = Path.Combine(projectDir, "Game.generated.cs");
            File.WriteAllText(sourcePath, sourceCode);

            string? icon = ResolveIcon(options, projectDir);

            string projectPath = Path.Combine(projectDir, "LiftedGame.csproj");
            File.WriteAllText(projectPath, BuildProject(safeName, icon, options));

            // `dotnet publish -o <dir>` forwards the path to MSBuild as a
            // property, and MSBuild splits property values on commas. A ROM named
            // "Legend of Zelda, The" therefore breaks publishing with MSB1006.
            // Publishing into a comma-free scratch directory and moving the
            // results afterwards sidesteps the parser entirely.
            publishDir = CreateScratchPublishDir();

            (bool ok, string output) = RunPublish(projectPath, publishDir, options, log);

            if (!ok)
            {
                return new PackageResult
                {
                    Success = false,
                    Error = output
                };
            }

            List<string> produced = MoveOutputs(publishDir, workDir, safeName);

            string launch = Path.Combine(workDir, safeName + ".exe");

            if (!File.Exists(launch))
            {
                // Self-contained publish can also land a single file under a
                // slightly different name; fall back to the dll.
                launch = Path.Combine(workDir, safeName + ".dll");
            }

            return new PackageResult
            {
                Success = File.Exists(launch),
                LaunchPath = launch,
                Produced = produced,
                Error = File.Exists(launch) ? string.Empty : "No launchable output produced."
            };
        }
        catch (Exception ex)
        {
            return new PackageResult
            {
                Success = false,
                Error = ex.GetType().Name + ": " + ex.Message
            };
        }
        finally
        {
            TryDeleteDirectory(projectDir);

            if (publishDir != null)
                TryDeleteDirectory(publishDir);
        }
    }

    /// <summary>
    /// A unique, comma-free scratch directory for <c>dotnet publish -o</c>.
    ///
    /// The temp root itself can contain a comma (a Windows user name is enough),
    /// so the candidate is validated and a comma-free fallback is used when it
    /// is not safe.
    /// </summary>
    private static string CreateScratchPublishDir()
    {
        string name = Guid.NewGuid().ToString("N");

        string[] roots =
        {
            Path.Combine(Path.GetTempPath(), "neslifter-publish"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NesLifter", "publish"),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "NesLifterPublish")
        };

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || root.IndexOf(',') >= 0 || root.IndexOf(';') >= 0)
                continue;

            string dir = Path.Combine(root, name);

            try
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception)
            {
                // Try the next candidate.
            }
        }

        throw new IOException("Could not create a comma-free scratch directory for publishing.");
    }

    /// <summary>
    /// Chooses the icon to embed: explicit path, then a previously captured
    /// <c>.icon.ico</c> next to the game, then the lifter's own default.
    /// </summary>
    private static string? ResolveIcon(PackageOptions options, string projectDir)
    {
        List<string> candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.IconPath))
            candidates.Add(options.IconPath!);

        candidates.Add(Path.Combine(Path.GetDirectoryName(projectDir)!, options.AssemblyName + ".icon.ico"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico"));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                // Copy next to the project: MSBuild resolves ApplicationIcon
                // relative to the project file.
                string target = Path.Combine(projectDir, "app.ico");

                try
                {
                    File.Copy(candidate, target, overwrite: true);
                    return "app.ico";
                }
                catch (Exception)
                {
                    // Try the next candidate.
                }
            }
        }

        return null;
    }

    private static string BuildProject(string assemblyName, string? icon, PackageOptions options)
    {
        string tfm = "net" + Environment.Version.Major + ".0-windows";
        bool selfContained = options.Mode == PackageMode.SelfContained;

        StringBuilder sb = new StringBuilder();

        sb.Append(Raw($$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>{{tfm}}</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <AssemblyName>{{Escape(assemblyName)}}</AssemblyName>
                <RootNamespace>Lifted</RootNamespace>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <Nullable>disable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <LangVersion>latest</LangVersion>
                <DebugType>none</DebugType>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>
                <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
                <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
                <!-- Machine-generated code: silence the noise the SDK would
                     otherwise print for every unreachable goto and unused field. -->
                <NoWarn>$(NoWarn);CS0162;CS0168;CS0219;CS0414;CS0649;CS8509;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn>
            """));

        if (icon != null)
            sb.AppendLine($"    <ApplicationIcon>{Escape(icon)}</ApplicationIcon>");

        if (selfContained)
        {
            sb.Append(Raw($$"""
                    <RuntimeIdentifier>{{Escape(options.RuntimeIdentifier)}}</RuntimeIdentifier>
                    <SelfContained>true</SelfContained>
                    <PublishSingleFile>true</PublishSingleFile>
                    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
                    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
                """));
        }

        sb.Append(Raw("""
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Game.generated.cs" />
              </ItemGroup>
            </Project>

            """));

        return sb.ToString();
    }

    /// <summary>
    /// Appends text verbatim, normalising line endings. Keeps the project XML
    /// above readable as XML instead of a wall of escaped quotes.
    /// </summary>
    private static string Raw(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine;
    }

    private static (bool Ok, string Output) RunPublish(
        string projectPath, string publishDir, PackageOptions options, Action<string> log)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };

        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(publishDir);
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add(options.Verbose ? "-v=minimal" : "-v=quiet");

        try
        {
            using Process process = Process.Start(psi)!;

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (false, "dotnet publish timed out.");
            }

            string outText = stdout.ToString();
            string errText = stderr.ToString();

            if (options.Verbose && outText.Length > 0)
                log(outText.TrimEnd());

            if (process.ExitCode == 0)
                return (true, outText);

            StringBuilder error = new StringBuilder();
            error.AppendLine("dotnet publish failed (exit " + process.ExitCode + ").");

            foreach (string line in (outText + "\n" + errText).Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.Contains(" error ", StringComparison.OrdinalIgnoreCase))
                    error.AppendLine(trimmed);
            }

            return (false, error.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return (false, "Could not run dotnet publish: " + ex.Message +
                           " (is the .NET SDK installed?)");
        }
    }

    /// <summary>
    /// Moves publish output into the work directory root, replacing previous
    /// builds. Returns the paths that survived.
    /// </summary>
    private static List<string> MoveOutputs(string publishDir, string workDir, string safeName)
    {
        List<string> produced = new List<string>();

        if (!Directory.Exists(publishDir))
            return produced;

        foreach (string file in Directory.GetFiles(publishDir))
        {
            string name = Path.GetFileName(file);
            string target = Path.Combine(workDir, name);

            try
            {
                if (File.Exists(target))
                    File.Delete(target);

                File.Move(file, target);
                produced.Add(target);
            }
            catch (Exception)
            {
                // A locked file (game still running) must not abort the lift.
            }
        }

        // The publish output may also contain a runtime folder; ignore it.
        produced.Sort(StringComparer.OrdinalIgnoreCase);
        return produced;
    }

    /// <summary>
    /// Makes a ROM name safe to use as an assembly name.
    ///
    /// Commas are allowed on purpose: they are legal in an assembly name and
    /// common in GoodNES titles ("Legend of Zelda, The"). Only characters that
    /// actually break MSBuild or the file system are replaced.
    /// </summary>
    private static string SanitizeAssemblyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Game";

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string result = sb.ToString().Trim().TrimEnd('.');

        return result.Length == 0 ? "Game" : result;
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception)
        {
            // Leftover build intermediates are harmless.
        }
    }

    /// <summary>True when a .NET SDK is available to publish with.</summary>
    public static bool IsSdkAvailable()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("--list-sdks");

            using Process process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(15000))
            {
                try { process.Kill(true); } catch { }
                return false;
            }

            return process.ExitCode == 0 && output.Trim().Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
