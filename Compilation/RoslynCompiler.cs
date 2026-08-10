using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace NesLifter.Compilation;

public sealed class RoslynCompiler
{
    public bool Compile(
        string sourceCode,
        string outputPath,
        Action<string> log)
    {
        try
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                sourceCode,
                new CSharpParseOptions(LanguageVersion.Latest));

            // В современном .NET правильный способ получить базовые сборки —
            // использовать TRUSTED_PLATFORM_ASSEMBLIES.
            string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

            if (string.IsNullOrWhiteSpace(tpa))
            {
                log("TRUSTED_PLATFORM_ASSEMBLIES is empty. Roslyn compilation cannot reference base assemblies.");
                return false;
            }

            var references = tpa
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToList();

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(outputPath),
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(
                    OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release));

            using FileStream stream = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write);

            EmitResult result = compilation.Emit(stream);

            if (!result.Success)
            {
                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        log($"[Roslyn Error] {diagnostic.Id}: {diagnostic.GetMessage()}");
                    }
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            log("Roslyn compilation exception:");
            log(ex.ToString());
            return false;
        }
    }
}