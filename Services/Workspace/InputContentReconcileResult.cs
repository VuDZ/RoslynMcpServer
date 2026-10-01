namespace RoslynMcpServer.Services.Workspace;

/// <summary>One path the queue processor compared to the committed bytes of its generation.</summary>
internal sealed class InputContentReconcileResult
{
    public string CanonicalPath { get; }

    public ContentReconcileOutcome Outcome { get; }

    public InputContentReconcileResult(string canonicalPath, ContentReconcileOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        CanonicalPath = canonicalPath;
        Outcome = outcome;
    }
}
