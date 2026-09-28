namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Why the walk left a construct unfollowed. A check built on the walk treats the two kinds differently,
/// so the reason is recorded as a value rather than read out of the limit text: a delegate member whose
/// target is registered outside the declared scope is the deliberate shape of the production test seam,
/// while a delegate call the walk could not attribute to a member of the scope is a call site that no
/// other rule of such a check can see.
/// </summary>
internal enum ReachabilityLimitKind
{
    /// <summary>
    /// An invoked delegate member has no assignment inside the declared scope: the walk did follow the
    /// call to the member and found no target for it there.
    /// </summary>
    TargetOutsideTheScope,

    /// <summary>
    /// A delegate call the walk cannot attribute to a delegate member of the scope: a computed receiver
    /// (<c>GetSeam().Invoke()</c>, <c>_seams[0].Invoke()</c>) or a delegate-valued expression
    /// (<c>GetSeam()()</c>). The call site itself is what could not be resolved.
    /// </summary>
    DelegateReceiverNotInScope,
}
