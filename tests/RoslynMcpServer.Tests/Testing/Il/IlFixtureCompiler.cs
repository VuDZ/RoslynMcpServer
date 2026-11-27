using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Compiles the source of a fixture assembly into a file, so the IL tests do not depend on the build output
/// of this repository. No PDB is written: the product does not read one.
/// </summary>
internal static class IlFixtureCompiler
{
    /// <summary>
    /// Compiles without optimization: the compiler then keeps a local variable that is only assigned, which
    /// the local-variable edge needs.
    /// </summary>
    /// <returns>The path of the assembly file in <paramref name="directory"/>.</returns>
    public static string Compile(string assemblyName, string source, string directory, params string[] references)
    {
        Directory.CreateDirectory(directory);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            FrameworkReferences().Concat(references.Select(path => MetadataReference.CreateFromFile(path))),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        var path = Path.Combine(directory, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        if (!emit.Success)
        {
            var errors = emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString());
            throw new InvalidOperationException($"Fixture assembly {assemblyName} did not compile: {string.Join("; ", errors)}");
        }

        return path;
    }

    /// <summary>The runtime of the test process and xUnit, whose attributes the fixture test classes carry.</summary>
    private static IEnumerable<MetadataReference> FrameworkReferences()
    {
        var runtime = MetadataReferenceSets.Runtime();
        var xunit = typeof(FactAttribute).Assembly.Location;
        var hasXunit = runtime.Any(reference =>
            string.Equals(Path.GetFileName(reference.Display), Path.GetFileName(xunit), StringComparison.OrdinalIgnoreCase));
        return hasXunit ? runtime : runtime.Append(MetadataReference.CreateFromFile(xunit));
    }
}
