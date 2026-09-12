namespace RoslynMcpServer.Services;

/// <summary>
/// Opt-in overlay requires a complete same-session provenance snapshot.
/// Unsuitable capture is a load-boundary refusal, not a per-reference guess.
/// </summary>
internal static class AnalyzerProvenanceCaptureGate
{
    public const string ReasonMissing = "provenance-capture-missing";
    public const string ReasonFailed = "provenance-capture-failed";
    public const string ReasonIncomplete = "provenance-capture-incomplete";
    public const string ReasonSessionMismatch = "provenance-capture-session-mismatch";

    public static bool IsSuitableForOptInOverlay(AnalyzerProvenanceSnapshot? snapshot, Guid sessionId)
    {
        return TryGetUnsuitableReason(snapshot, sessionId) is null;
    }

    public static string? TryGetUnsuitableReason(AnalyzerProvenanceSnapshot? snapshot, Guid sessionId)
    {
        if (snapshot is null)
        {
            return ReasonMissing;
        }

        if (sessionId == Guid.Empty || snapshot.LoadSessionId != sessionId)
        {
            return ReasonSessionMismatch;
        }

        return snapshot.Status switch
        {
            AnalyzerProvenanceCaptureStatus.Complete => null,
            AnalyzerProvenanceCaptureStatus.Failed => ReasonFailed,
            AnalyzerProvenanceCaptureStatus.Incomplete => ReasonIncomplete,
            _ => ReasonFailed,
        };
    }
}
