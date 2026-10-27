namespace BuildHostFork.Trace;

/// <summary>
/// One operation the 5.9.0 loader reported through <c>IProgress&lt;ProjectLoadProgress&gt;</c>: Evaluate wraps the
/// LoadProjectFileAsync call, Build wraps GetProjectFileInfosAsync, Resolve wraps reference resolution, including the
/// loads of referenced projects it starts. Timestamps are Stopwatch ticks of the shared machine clock.
/// </summary>
internal sealed record ClientOperation(string Operation, string Path, string? TargetFramework, long Start, long End);
