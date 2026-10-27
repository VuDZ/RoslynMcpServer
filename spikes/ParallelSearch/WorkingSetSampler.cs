namespace ParallelSearch;

/// <summary>
/// Highest working set seen every 25 ms while a search runs. The process peak would include the load,
/// which is larger than the search and the same for every degree of parallelism.
/// </summary>
internal sealed class WorkingSetSampler : IDisposable
{
    public WorkingSetSampler()
    {
        _peak = Environment.WorkingSet;
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(25));
    }

    /// <summary>Stops sampling and returns the peak in bytes, including one final sample.</summary>
    public long Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Sample();
        return Interlocked.Read(ref _peak);
    }

    public void Dispose() => _timer.Dispose();

    private void Sample()
    {
        var current = Environment.WorkingSet;
        long seen;
        do
        {
            seen = Interlocked.Read(ref _peak);
        }
        while (current > seen && Interlocked.CompareExchange(ref _peak, current, seen) != seen);
    }

    private readonly Timer _timer;
    private long _peak;
}
