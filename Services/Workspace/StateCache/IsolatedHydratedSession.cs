using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Owns an isolated candidate and its image-backed metadata until disposal. This is a finite base-query
/// artifact: it grants no analyzer execution, emit, publication, write, or production cache admission.
/// </summary>
internal sealed class IsolatedHydratedSession : IDisposable
{
    public AdhocWorkspace Workspace { get; }

    public Solution Solution { get; }

    public IReadOnlyDictionary<SnapshotProjectKey, ProjectId> Projects { get; }

    public IReadOnlyDictionary<ProjectId, CSharpCompilation> BaseCompilations { get; }

    public IReadOnlyDictionary<string, ImmutableArray<HydratedInputOccurrence>> InputMap { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal IsolatedHydratedSession(
        AdhocWorkspace workspace,
        Solution solution,
        IReadOnlyDictionary<SnapshotProjectKey, ProjectId> projects,
        IReadOnlyDictionary<ProjectId, CSharpCompilation> baseCompilations,
        IReadOnlyDictionary<string, ImmutableArray<HydratedInputOccurrence>> inputMap,
        IEnumerable<Metadata> metadata,
        Action<AdhocWorkspace>? workspaceDisposed)
    {
        Workspace = workspace;
        Solution = solution;
        Projects = projects.ToImmutableDictionary(WorkspaceSnapshotCodec.ProjectKeyIdentity);
        BaseCompilations = baseCompilations.ToImmutableDictionary();
        InputMap = inputMap.ToImmutableDictionary(InputPathCanon.Comparer);
        _metadata = metadata.ToArray();
        _workspaceDisposed = workspaceDisposed;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Workspace.Dispose();
        }
        finally
        {
            foreach (var metadata in _metadata)
            {
                metadata.Dispose();
            }
        }
        _workspaceDisposed?.Invoke(Workspace);
    }

    private readonly Metadata[] _metadata;
    private readonly Action<AdhocWorkspace>? _workspaceDisposed;
    private int _disposed;
}
