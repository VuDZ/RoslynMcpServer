namespace RoslynMcpServer.Services.Testing.Snapshots;

/// <summary>
/// The cancellation of a first read that was retired under a lock. The caller calls <see cref="Cancel"/> after
/// it has left every lock it holds.
/// </summary>
/// <remarks>
/// <see cref="CancellationTokenSource.Cancel()"/> runs the registrations of the token synchronously. Called
/// under the baseline and snapshot locks, a registration or a synchronous continuation of an awaited
/// cancellable call would run inside both. The baseline cancels its own capture outside its lock for the same
/// reason.
/// </remarks>
internal readonly struct AssemblySnapshotRetirement
{
    public AssemblySnapshotRetirement(CancellationTokenSource? cancellation)
    {
        _cancellation = cancellation;
    }

    public void Cancel()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The worker disposes the source when it finishes; a finished read has nothing to cancel.
        }
    }

    private readonly CancellationTokenSource? _cancellation;
}
