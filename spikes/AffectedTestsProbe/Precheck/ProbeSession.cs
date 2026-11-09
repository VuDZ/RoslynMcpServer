using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace AffectedTestsProbe;

/// <summary>In-memory solution used by the precheck and the prototype tests. It is not this repository.</summary>
internal sealed class ProbeSession : IDisposable
{
    private readonly AdhocWorkspace _workspace;

    private ProbeSession(AdhocWorkspace workspace) => _workspace = workspace;

    public Solution Solution => _workspace.CurrentSolution;

    public static ProbeSession Open(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Probe", "Probe", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(projectId, References())
            .AddDocument(documentId, "Probe.cs", SourceText.From(source));
        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("Could not open the probe workspace.");
        }

        return new ProbeSession(workspace);
    }

    public void Dispose() => _workspace.Dispose();

    private static ImmutableArray<MetadataReference> References()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runtime = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var assembly in new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Task).Assembly,
            typeof(Xunit.FactAttribute).Assembly,
            typeof(Xunit.IAsyncLifetime).Assembly,
        })
        {
            Add(references, seen, assembly.Location);
        }

        Add(references, seen, Path.Combine(runtime, "System.Runtime.dll"));
        Add(references, seen, Path.Combine(runtime, "System.Threading.Tasks.dll"));
        return references.ToImmutable();
    }

    private static void Add(ImmutableArray<MetadataReference>.Builder references, HashSet<string> seen, string path)
    {
        if (path.Length > 0 && File.Exists(path) && seen.Add(path))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }
    }
}
