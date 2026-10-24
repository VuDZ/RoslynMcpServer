namespace BuildHostFork.Trace;

/// <summary>
/// How the hosts' request spans sit inside the client's Evaluate and Build operations. Lead is how long after the
/// operation started the host read the request, tail how long after the host sent the response the operation ended;
/// a negative value beyond the tolerance means the clocks do not agree.
/// </summary>
internal sealed record ClockCheckResult(int Matched, int Unmatched, int Violations, double MinLeadMs, double MinTailMs, double ToleranceMs);
