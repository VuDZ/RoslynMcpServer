namespace BuildHostFork.Trace;

/// <summary>
/// The trace of one BuildHost process. <see cref="ProcessStart"/> is an estimate on the Stopwatch clock: the process
/// start time comes from the wall clock and is placed relative to the start line's pair of wall and Stopwatch times.
/// </summary>
internal sealed record HostTrace(int ProcessId, long Frequency, long ProcessStart, IReadOnlyList<HostSpan> Spans);
