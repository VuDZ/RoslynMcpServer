namespace BuildHostFork.Trace;

/// <summary>An interval on the Stopwatch clock labelled with what happened in it; host segments win over client ones.</summary>
internal readonly record struct TimeSegment(long Start, long End, string Label, bool IsHost);
