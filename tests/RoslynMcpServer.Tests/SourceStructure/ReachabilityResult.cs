namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Outcome of a reachability query. An empty path with no diagnostics and no limits means "not reachable
/// within the analyzed scope"; a non-empty <see cref="Diagnostics"/> means the answer is undecidable (an
/// unresolved call, a delegate target that does not resolve, a truncated walk) and must not be read as
/// absence. <see cref="Limits"/> records what the walk could not follow by design, each entry stating
/// whether the call site itself was understood (see <see cref="ReachabilityLimitKind"/>): such a walk is
/// not decided either, because a target it never saw could still be the forbidden call.
/// </summary>
internal sealed class ReachabilityResult
{
    /// <summary>Executed call edges from the entry to the target; empty when the target was not reached.</summary>
    public IReadOnlyList<SourceCallStep> Path { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>
    /// Recorded limits of the walk, such as a delegate member with no assignment in the scope or a
    /// delegate call the walk cannot attribute to such a member. Each one names the construct it could
    /// not follow and states why, so a caller can report it without re-deriving the reason.
    /// </summary>
    public IReadOnlyList<ReachabilityLimit> Limits { get; }

    public ReachabilityResult(
        IReadOnlyList<SourceCallStep> path,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<ReachabilityLimit> limits)
    {
        Path = path;
        Diagnostics = diagnostics;
        Limits = limits;
    }

    public bool IsReachable => Path.Count > 0;

    /// <summary>
    /// True when the walk completed with nothing left unfollowed, which is what makes an empty path
    /// evidence of absence. A diagnostic is a failed call site the walk could not decide; a limit is a
    /// construct it could not follow by design. Both mean the answer is "not shown", not "no".
    /// </summary>
    public bool IsDecided => Diagnostics.Count == 0 && Limits.Count == 0;

    /// <summary>Path as <c>Caller -&gt; Callee at file:line</c> steps joined in call order.</summary>
    public string DescribePath() => Path.Count == 0
        ? "<not reached>"
        : string.Join(
            " -> ",
            Path.Select(step =>
                $"{step.Caller.ContainingType.Name}.{step.Caller.Name} -> {step.Callee.ContainingType.Name}.{step.Callee.Name} at {step.FilePath}:{step.Line}"));
}
