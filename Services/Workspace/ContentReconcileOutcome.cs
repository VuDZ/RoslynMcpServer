namespace RoslynMcpServer.Services.Workspace;

/// <summary>What a byte comparison decided for one queued content event.</summary>
internal enum ContentReconcileOutcome
{
    None = 0,
    LeftPending,
    Echo,
    Changed,
}
