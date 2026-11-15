using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace NesLifter.Compilation;

/// <summary>Result of one compilation attempt.</summary>
public sealed class CompileResult
{
    public bool Success { get; init; }

    /// <summary>Absolute path of the emitted assembly.</summary>
    public string AssemblyPath { get; init; } = string.Empty;

    /// <summary>Absolute path of the emitted portable PDB, if any.</summary>
    public string PdbPath { get; init; } = string.Empty;

    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }

    /// <summary>All errors and warnings, already formatted.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Compiles the generated C# into a runnable .NET assembly using Roslyn.
///
/// Design notes:
/// * References come from TRUSTED_PLATFORM_ASSEMBLIES, so the emitted game
///   targets the same runtime the lifter runs on and needs no extra probing.
/// * A portable PDB is embedded next to the assembly, so a stack trace from a
///   generated game points at real line numbers in <c>Game.generated.cs</c>.
/// * Emit is deterministic: the same ROM always produces the same bytes.
/// * Every diagnostic is surfaced, not just errors, so warnings in generated
///   code are visible instead of silently ignored.
/// </summary>
public sealed class RoslynCompiler
{
    private readonly object _sync = new();
    private List<MetadataReference>? _cachedReferences;

    /// <summary>
    /// Compiles <paramref name="sourceCode"/> to <paramref name="outputPath"/>.
    /// </summary>
    public bool Compile(string sourceCode, string outputPath, Action<string> log)
    {
        return CompileDetailed(sourceCode, outputPath, log).Success;
    }

    /// <summary>
    /// Compiles and returns the full result, including warnings.
    /// </summary>
    public CompileResult CompileDetailed(
        string sourceCode,
        string outputPath,
        Action<string> log,
        string sourceFileName = "Game.generated.cs",
        bool emitPdb = true)
    {
        if (sourceCode == null)
            throw new ArgumentNullException(nameof(sourceCode));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        List<string> diagnostics = new List<string>();

        try
        {
            List<MetadataReference> references = GetReferences(log);

            if (references.Count == 0)
            {
                log("No reference assemblies available; cannot compile.");
                return new CompileResult { Success = false, Diagnostics = diagnostics };
            }

            CSharpParseOptions parseOptions = new CSharpParseOptions(
                LanguageVersion.Latest,
                DocumentationMode.None,
                SourceCodeKind.Regular);

            // Parse with a real path so PDB sequence points map onto the file
            // that is saved next to the assembly.
            SourceText sourceText = SourceText.From(sourceCode, System.Text.Encoding.UTF8);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                parseOptions,
                path: sourceFileName);

            string assemblyName = Path.GetFileNameWithoutExtension(outputPath);

            CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                deterministic: true,
                concurrentBuild: true,
                platform: Platform.AnyCpu,
                mainTypeName: null,
                nullableContextOptions: NullableContextOptions.Disable,
                warningLevel: 4,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    // Generated code is machine produced; these are noise.
                    ["CS0162"] = ReportDiagnostic.Suppress,   // unreachable code
                    ["CS0168"] = ReportDiagnostic.Suppress,   // unused variable
                    ["CS0219"] = ReportDiagnostic.Suppress,   // assigned but never used
                    ["CS0414"] = ReportDiagnostic.Suppress,   // field assigned but never used
                    ["CS0649"] = ReportDiagnostic.Suppress,   // never assigned
                    ["CS8509"] = ReportDiagnostic.Suppress,   // switch not exhaustive
                    ["CS8600"] = ReportDiagnostic.Suppress,
                    ["CS8601"] = ReportDiagnostic.Suppress,
                    ["CS8602"] = ReportDiagnostic.Suppress,
                    ["CS8603"] = ReportDiagnostic.Suppress,
                    ["CS8604"] = ReportDiagnostic.Suppress,
                    ["CS8618"] = ReportDiagnostic.Suppress,
                    ["CS8625"] = ReportDiagnostic.Suppress
                });

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                compilationOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

            string pdbPath = Path.ChangeExtension(outputPath, ".pdb");
            EmitOptions emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);

            using FileStream assemblyStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using FileStream? pdbStream = emitPdb
                ? new FileStream(pdbPath, FileMode.Create, FileAccess.Write)
                : null;

            EmitResult result = compilation.Emit(
                assemblyStream,
                pdbStream,
                options: emitOptions);

            int errors = 0;
            int warnings = 0;

            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                    errors++;
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    warnings++;

                if (diagnostic.Severity == DiagnosticSeverity.Error ||
                    diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    string text = Format(diagnostic);
                    diagnostics.Add(text);

                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        log(text);
                }
            }

            if (!result.Success)
            {
                log("Roslyn compilation failed: " + errors + " error(s).");
                TryDelete(outputPath);
                TryDelete(pdbPath);

                return new CompileResult
                {
                    Success = false,
                    ErrorCount = errors,
                    WarningCount = warnings,
                    Diagnostics = diagnostics
                };
            }

            if (warnings > 0)
                log("Roslyn: " + warnings + " warning(s) in generated code.");

            return new CompileResult
            {
                Success = true,
                AssemblyPath = Path.GetFullPath(outputPath),
                PdbPath = emitPdb ? Path.GetFullPath(pdbPath) : string.Empty,
                ErrorCount = errors,
                WarningCount = warnings,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            log("Roslyn compilation exception:");
            log(ex.ToString());

            return new CompileResult
            {
                Success = false,
                ErrorCount = 1,
                Diagnostics = diagnostics
            };
        }
    }

    /// <summary>
    /// Reference set for the generated game: the shared framework plus the
    /// desktop (WinForms/Drawing) framework when the lifter itself runs on it.
    /// </summary>
    private List<MetadataReference> GetReferences(Action<string> log)
    {
        lock (_sync)
        {
            if (_cachedReferences != null)
                return _cachedReferences;

            string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

            if (string.IsNullOrWhiteSpace(tpa))
            {
                log("TRUSTED_PLATFORM_ASSEMBLIES is empty; cannot resolve reference assemblies.");
                _cachedReferences = new List<MetadataReference>();
                return _cachedReferences;
            }

            List<MetadataReference> references = new List<MetadataReference>();

            foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (path.Length == 0 || !File.Exists(path))
                    continue;

                // Only managed assemblies are valid metadata references.
                if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (Exception)
                {
                    // Native or malformed image; skip it.
                }
            }

            _cachedReferences = references;
            return references;
        }
    }

    private static string Format(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        string position = diagnostic.Location.IsInSource
            ? Path.GetFileName(span.Path) + "(" + (span.StartLinePosition.Line + 1) + "," +
              (span.StartLinePosition.Character + 1) + ")"
            : "<no location>";

        return "[Roslyn " + diagnostic.Severity + "] " + diagnostic.Id + " " + position + ": " +
               diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // A locked file is not worth failing over.
        }
    }
}
