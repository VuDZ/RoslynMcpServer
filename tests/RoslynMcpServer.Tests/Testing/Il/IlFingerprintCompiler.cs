using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Tests.SourceStructure;

namespace RoslynMcpServer.Tests.Testing.Il;

internal static class IlFingerprintCompiler
{
    public static IlAssemblyImage Compile(
        string source,
        string name = "Fixture",
        IEnumerable<MetadataReference>? references = null,
        byte[]? resource = null,
        string path = "missing/Fixture.dll",
        bool pdb = false,
        string resourceName = "data")
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path + ".cs", encoding: System.Text.Encoding.UTF8);
        var compilation = CSharpCompilation.Create(
            name,
            [tree],
            MetadataReferenceSets.Runtime().Concat(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, deterministic: true, allowUnsafe: true));
        using var output = new MemoryStream();
        using var symbols = pdb ? new MemoryStream() : null;
        ResourceDescription[] resources = resource is null ? [] :
            [new ResourceDescription(resourceName, () => new MemoryStream(resource), isPublic: true)];
        var result = compilation.Emit(
            output,
            pdbStream: symbols,
            manifestResources: resources,
            options: pdb ? new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb) : null);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return new IlAssemblyImage(path, [.. output.ToArray()]);
    }

    public static MetadataReference Reference(IlAssemblyImage image, string? alias = null) =>
        MetadataReference.CreateFromImage(image.Content,
            alias is null ? MetadataReferenceProperties.Assembly :
                MetadataReferenceProperties.Assembly.WithAliases([alias]));
}
