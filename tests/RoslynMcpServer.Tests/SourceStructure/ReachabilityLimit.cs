namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// One construct the walk could not follow: the reason as a <see cref="ReachabilityLimitKind"/> and a
/// <see cref="Text"/> naming the construct and its site. Both are part of the result —
/// <see cref="ReachabilityResult.IsDecided"/> counts every recorded limit, and a caller that has to treat
/// one reason differently from the other reads the kind instead of the message.
/// </summary>
internal sealed record ReachabilityLimit(ReachabilityLimitKind Kind, string Text)
{
    /// <summary>Limit of a delegate member whose targets are registered outside the declared scope.</summary>
    public static ReachabilityLimit TargetOutsideTheScope(string text) =>
        new(ReachabilityLimitKind.TargetOutsideTheScope, text);

    /// <summary>Limit of a delegate call the walk cannot attribute to a delegate member of the scope.</summary>
    public static ReachabilityLimit DelegateReceiverNotInScope(string text) =>
        new(ReachabilityLimitKind.DelegateReceiverNotInScope, text);
}
