using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tests.SourceStructure;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.AssemblyResolve;

/// <summary>
/// The target assembly references a dependency that exists neither next to it nor in the NuGet cache,
/// like Microsoft.CodeAnalysis.Workspaces and its undeclared SQLitePCLRaw reference. Decompilation must
/// still succeed and name the missing dependency instead of failing the whole assembly.
/// </summary>
public sealed class DecompilerUnresolvedReferenceTests : IDisposable
{
    private static string ExpectedNote =>
        $"// Unresolved assembly references, their types are shown as unknown: {DependencyName} 1.2.3.4.";

    public DecompilerUnresolvedReferenceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "roslyn-mcp-decompiler-unresolved", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _targetPath = EmitTargetWithoutDependency(_directory);
        _tools = new CodeAnalysisTools(SolutionManagerTestFactory.Create(), NullLogger<CodeAnalysisTools>.Instance);
    }

    [Fact]
    public async Task Method_body_is_decompiled_and_names_the_missing_dependency()
    {
        var result = await _tools.GetDecompiledMethodBody(
            assemblyPath: _targetPath,
            fullTypeName: "DecompileTarget.Consumer",
            methodName: "Measure");

        Assert.Contains("public int Measure(", result, StringComparison.Ordinal);
        Assert.EndsWith(ExpectedNote, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_is_decompiled_and_names_the_missing_dependency()
    {
        var result = await _tools.DecompileType(assemblyPath: _targetPath, fullTypeName: "DecompileTarget.Consumer");

        Assert.Contains("public class Consumer", result, StringComparison.Ordinal);
        Assert.EndsWith(ExpectedNote, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_is_empty_when_every_reference_was_resolved()
    {
        Assert.Equal(string.Empty, DecompilerHost.FormatUnresolvedReferencesNote([]));
    }

    [Fact]
    public void Note_lists_at_most_ten_references()
    {
        var references = Enumerable.Range(1, 12).Select(index => $"Missing{index:D2} 1.0.0.0").ToList();

        var note = DecompilerHost.FormatUnresolvedReferencesNote(references);

        Assert.Contains("Missing10 1.0.0.0 and 2 more.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing11", note, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked file only affects cleanup of a temporary directory.
        }
    }

    /// <summary>
    /// Emits only the target assembly; the dependency exists as an in-memory reference during compilation.
    /// A unique dependency name keeps the NuGet fallback from finding a namesake in the package cache.
    /// </summary>
    private static string EmitTargetWithoutDependency(string directory)
    {
        var dependency = Compile(
            DependencyName,
            """
            [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
            namespace MissingDependency;
            public class Widget
            {
                public int Size => 1;
            }
            """,
            []);
        var target = Compile(
            "DecompileTarget",
            """
            namespace DecompileTarget;
            public class Consumer
            {
                public int Measure(MissingDependency.Widget widget) => widget.Size + 1;
            }
            """,
            [dependency.ToMetadataReference()]);

        var path = Path.Combine(directory, "DecompileTarget.dll");
        var emit = target.Emit(path);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics));
        return path;
    }

    private static CSharpCompilation Compile(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            MetadataReferenceSets.Runtime().Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
        return compilation;
    }

    // One name per test run: the NuGet fallback caches lookups by name, so later tests skip the cache scan.
    private static readonly string DependencyName = "RoslynMcpMissingDependency" + Guid.NewGuid().ToString("N");

    private readonly string _directory;
    private readonly string _targetPath;
    private readonly CodeAnalysisTools _tools;
}
