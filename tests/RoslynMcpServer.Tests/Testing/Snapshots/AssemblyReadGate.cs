using RoslynMcpServer.Services.Testing.Il;

namespace RoslynMcpServer.Tests.Testing.Snapshots;

/// <summary>Holds the first read of one assembly until the test releases it.</summary>
internal sealed class AssemblyReadGate : IDisposable
{
    internal Task Entered => _entered.Task;

    internal AssemblyReadGate(string path) => _path = path;

    public void Dispose() => Release();

    internal async Task WaitAsync(string path, CancellationToken cancellationToken)
    {
        if (!IlPaths.Comparer.Equals(path, _path))
        {
            return;
        }

        _entered.TrySetResult();
        // Cancellation is ignored on purpose: a late result must be rejected by the session, not by timing.
        await _release.Task;
    }

    internal void Release() => _release.TrySetResult();

    private readonly string _path;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
