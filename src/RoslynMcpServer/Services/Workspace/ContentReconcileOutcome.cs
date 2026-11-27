namespace RoslynMcpServer.Services.Workspace;

/// <summary>What a byte comparison decided for one queued content event.</summary>
internal enum ContentReconcileOutcome
{
    None = 0,
    LeftPending,
    Echo,
    Changed,

    /// <summary>
    /// The byte read overlapped a newer content event. <c>ApplyContentObservation</c> still
    /// returns <see cref="LeftPending"/> for that case; only the reconciler promotes it so one
    /// flush can read the path again without publishing a read it refused.
    /// </summary>
    RevisionMoved,
}
