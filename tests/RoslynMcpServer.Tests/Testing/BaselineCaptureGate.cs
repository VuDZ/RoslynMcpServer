using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Tests.Testing;

internal sealed class BaselineCaptureGate : IDisposable
{
    internal Task Entered => _entered.Task;

    internal BaselineCaptureGate(string path) => _path = path;

    public void Dispose() => Release();

    internal async Task WaitAsync(BaselineDocumentKey key, CancellationToken cancellationToken)
    {
        if (!InputPathCanon.Comparer.Equals(key.DocumentPath, _path))
        {
            return;
        }

        _entered.TrySetResult();
        // Ignore cancellation deliberately: stale completion must be rejected by publication admission, not timing.
        await _release.Task;
    }

    internal void Release() => _release.TrySetResult();

    private readonly string _path;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
