namespace BuildHostFork.Trace;

/// <summary>
/// How the hosts' request spans sit inside the client's Evaluate and Build operations. Lead is how long after the
/// operation started the host read the request, tail how long after the host began sending the response the
/// operation ended; a negative value beyond the tolerance means the clocks do not agree.
/// </summary>
/// <remarks>
/// <see cref="Untraced"/> operations have no request of their method and path in any trace: their projects went to
/// a host that does not trace. <see cref="OutsideOperation"/> operations have such requests, but none inside the
/// operation, and <see cref="UnclaimedRequests"/> are Evaluate and Build requests no operation took; both mean the
/// clocks or the traces disagree, like <see cref="Violations"/>.
/// </remarks>
internal sealed record ClockCheckResult(
    int Matched,
    int OutsideOperation,
    int UnclaimedRequests,
    int Violations,
    double MinLeadMs,
    double MinTailMs,
    double ToleranceMs,
    IReadOnlyList<UntracedOperation> Untraced)
{
    /// <summary>True when every traced request lies inside its operation; untraced operations are allowed.</summary>
    public bool Agrees => OutsideOperation == 0 && UnclaimedRequests == 0 && Violations == 0;
}
