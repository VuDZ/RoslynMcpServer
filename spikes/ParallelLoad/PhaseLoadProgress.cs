using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.MSBuild;

namespace ParallelLoad;

internal sealed class PhaseLoadProgress(TimeProvider timeProvider, PhaseLoadProgress? parent = null, long? origin = null) : IProgress<ProjectLoadProgress>
{
    public double? FirstProgressMs { get; private set; }
    public double ElapsedMs { get; private set; }
    public double? StartOffsetMs { get; private set; }
    public double? EndOffsetMs { get; private set; }
    public string[] LoadedPaths => _loadedPaths.Keys.Order(StringComparer.Ordinal).ToArray();
    public string[] Events => _events.ToArray();

    public void Start()
    {
        _start = timeProvider.GetTimestamp();
        StartOffsetMs = origin is { } timestamp ? timeProvider.GetElapsedTime(timestamp, _start).TotalMilliseconds : null;
    }

    public void Stop()
    {
        ElapsedMs = timeProvider.GetElapsedTime(_start).TotalMilliseconds;
        EndOffsetMs = StartOffsetMs + ElapsedMs;
    }

    public void Report(ProjectLoadProgress value)
    {
        ObservePath(value.FilePath);
        _events.Enqueue(System.Text.Json.JsonSerializer.Serialize(new
        {
            value.FilePath, Operation = value.Operation.ToString(), ElapsedMs = value.ElapsedTime.TotalMilliseconds,
        }));
        parent?.Report(value);
    }

    internal void ObservePath(string path)
    {
        lock (_gate)
        {
            FirstProgressMs ??= timeProvider.GetElapsedTime(_start).TotalMilliseconds;
        }

        _loadedPaths.TryAdd(Path.GetFullPath(path), 0);
    }

    private readonly ConcurrentDictionary<string, byte> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _events = new();
    private readonly object _gate = new();
    private long _start;
}
