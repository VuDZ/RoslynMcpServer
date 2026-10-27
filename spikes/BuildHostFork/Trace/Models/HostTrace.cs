namespace BuildHostFork.Trace;

/// <summary>
/// The trace of one BuildHost process. <see cref="ProcessStart"/> is an estimate on the Stopwatch clock: the process
/// start time comes from the wall clock and is placed relative to the start line's pair of wall and Stopwatch times.
/// <see cref="TraceStart"/> is the Stopwatch time of the start line itself. <see cref="UnreadableLines"/> counts lines
/// that are not JSON, such as the last line of a host killed while writing it.
/// </summary>
internal sealed record HostTrace(
    int ProcessId,
    long Frequency,
    long ProcessStart,
    long TraceStart,
    int UnreadableLines,
    IReadOnlyList<HostSpan> Spans);
