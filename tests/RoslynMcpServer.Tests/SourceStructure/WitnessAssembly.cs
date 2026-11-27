using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Isolated copy of a witness source, compiled into its own assembly on disk. The mutation under test is
/// a change of the source text, not a switch inside a production API, so the mutated code never reaches
/// the server assembly. The directory is removed on dispose; the host loads the assembly from a stream,
/// so no file lock survives a killed host.
/// </summary>
internal sealed class WitnessAssembly : IDisposable
{
    public string Path { get; }

    private readonly string _directory;

    private WitnessAssembly(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    /// <summary>
    /// Compiles <paramref name="sourceText"/> against the framework references of the test process.
    /// A source that does not compile throws with the compiler diagnostics: a timeout on a non-compiling
    /// mutation would prove nothing.
    /// </summary>
    public static WitnessAssembly Compile(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(SourceFile.Normalize(sourceText), path: SanitizedEntryDemoSource.Path);
        var compilation = CSharpCompilation.Create(
            "DeadlockWitnessDemo",
            new[] { tree },
            MetadataReferenceSets.Runtime(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()}")
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("witness source did not compile: " + string.Join("; ", errors));
        }

        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roslyn-mcp-deadlock-witness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "DeadlockWitnessDemo.dll");
        var emit = compilation.Emit(path);
        if (!emit.Success)
        {
            var failures = string.Join("; ", emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"witness assembly emit failed: {failures}");
        }

        return new WitnessAssembly(directory, path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A loaded or locked file only affects cleanup of a temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
