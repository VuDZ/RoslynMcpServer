using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed class ExperimentSession : IDisposable
{
    public Guid SessionId { get; } = Guid.NewGuid();

    public ExperimentRequest Request { get; }

    public Solution Solution
    {
        get
        {
            ThrowIfDisposed();
            return _solution;
        }
    }

    public DtbMeasurement Dtb
    {
        get
        {
            ThrowIfDisposed();
            return _counter.Snapshot();
        }
    }

    public ImmutableArray<WorkspaceDiagnostic> LoadDiagnostics { get; }

    public WorkspaceWriteOperationContext WriteContext { get; private set; }

    public bool AnalyzerAdmissionReady { get; internal set; }

    internal Microsoft.CodeAnalysis.Workspace Workspace { get; }

    internal SemaphoreSlim OperationLock { get; } = new(1, 1);

    internal IAnalyzerAssemblyLoader AnalyzerLoader { get; set; } = new RejectingLoader();

    internal AdmissionEvidence? Admission { get; set; }

    internal ExperimentSession(
        Microsoft.CodeAnalysis.Workspace workspace,
        ExperimentRequest request,
        DtbEventCounter counter,
        ImmutableArray<WorkspaceDiagnostic> loadDiagnostics)
    {
        Workspace = workspace;
        Request = request;
        _counter = counter;
        LoadDiagnostics = loadDiagnostics;
        _solution = workspace.CurrentSolution;
        WriteContext = CreateWriteContext();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        OperationLock.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var failures = new List<Exception>();
            foreach (var resource in new[] { Workspace }.Cast<IDisposable>().Concat(_resources.AsEnumerable().Reverse()))
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            _counter.Shutdown();
            if (failures.Count != 0)
            {
                throw new AggregateException("Experiment resource disposal failed.", failures);
            }
        }
        finally
        {
            OperationLock.Release();
        }
    }

    internal void Publish(Solution solution)
    {
        ThrowIfDisposed();
        if (OperationLock.CurrentCount != 0 || !ReferenceEquals(solution.Workspace, Workspace))
        {
            throw new InvalidOperationException("Publication requires the owned workspace and operation lock.");
        }

        _solution = solution;
        _rawRevision++;
        WriteContext = CreateWriteContext();
    }

    internal void Own(IDisposable resource) => _resources.Add(resource);

    internal void MarkDtbCoverageUnknown() => _counter.MarkCoverageUnknown();

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private WorkspaceWriteOperationContext CreateWriteContext() => WorkspaceWriteOperationContext.Verified(
        SessionId,
        Request.WorkspacePath,
        mapping: null,
        baseSnapshot: _solution,
        rawWorkspaceSnapshot: _solution,
        rawWorkspaceRevision: _rawRevision,
        shadowCopyEnabled: false);

    private sealed class RejectingLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) => throw new InvalidDataException("Analyzer is not admitted.");

        public System.Reflection.Assembly LoadFromPath(string fullPath) => throw new InvalidDataException("Analyzer is not admitted.");
    }

    private readonly DtbEventCounter _counter;
    private readonly List<IDisposable> _resources = [];
    private Solution _solution;
    private long _rawRevision;
    private bool _disposed;
}
