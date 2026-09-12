using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

public enum WorkspaceWriteStatus
{
    FullSuccess,
    PreflightRejected,
    PartialPersistence,
    ReconciliationSucceeded,
    ReconciliationFailed,
    Cancelled,
    Skipped,
}

/// <summary>
/// Structured internal write outcome. Adapters format this as existing markdown;
/// this revision does not add a public MCP schema.
/// </summary>
public sealed class WorkspaceWriteResult
{
    public static WorkspaceWriteResult Skipped(string reason) => new()
    {
        Status = WorkspaceWriteStatus.Skipped,
        Reason = reason,
    };

    public static WorkspaceWriteResult PreflightRejected(string reason) => new()
    {
        Status = WorkspaceWriteStatus.PreflightRejected,
        Reason = reason,
    };

    public WorkspaceWriteStatus Status { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> SavedPaths { get; init; } = Array.Empty<string>();

    public bool WorkspaceApplied { get; init; }

    public bool OverlayPublished { get; init; }

    public bool UnappliedProjectState { get; init; }

    public bool IsFullSuccess => Status == WorkspaceWriteStatus.FullSuccess;

    /// <summary>Path list for adapters that previously returned only written files.</summary>
    public IReadOnlyList<string> WrittenPathsForAdapters => SavedPaths;

    public string FormatAdapterMessage(string successPrefix)
    {
        if (Status == WorkspaceWriteStatus.FullSuccess)
        {
            return successPrefix + $" Files touched: {SavedPaths.Count}.";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("Workspace write ").Append(Status);
        if (!string.IsNullOrWhiteSpace(Reason))
        {
            sb.Append(": ").Append(Reason);
        }
        else
        {
            sb.Append('.');
        }

        sb.Append(" Saved paths (").Append(SavedPaths.Count).Append("):");
        if (SavedPaths.Count == 0)
        {
            sb.Append(" (none).");
        }
        else
        {
            sb.AppendLine();
            foreach (var path in SavedPaths)
            {
                sb.Append("- ").AppendLine(path);
            }
        }

        sb.Append(" Unapplied project state: ").Append(UnappliedProjectState);
        sb.Append(". Overlay published: ").Append(OverlayPublished).Append('.');
        if (Status is WorkspaceWriteStatus.ReconciliationFailed or WorkspaceWriteStatus.PartialPersistence)
        {
            sb.Append(" Disk freshness is not confirmed for the full request.");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Mapping, session, published base, and raw-workspace stamp the candidate was built with.
/// Not a history of snapshots or an in-flight operation registry.
/// <see cref="BaseSnapshot"/> is the published overlay used for lookup/transform.
/// Freshness compares <see cref="RawWorkspaceRevision"/> / <see cref="RawWorkspaceSnapshot"/>,
/// never overlay versus raw via <c>ReferenceEquals</c>.
/// </summary>
internal sealed class WorkspaceWriteOperationContext
{
    private WorkspaceWriteOperationContext(
        Guid sessionId,
        string? loadedPath,
        AnalyzerShadowMapping? mapping,
        Solution? baseSnapshot,
        Solution? rawWorkspaceSnapshot,
        long rawWorkspaceRevision,
        bool shadowCopyEnabled,
        SemanticPublicationAdmission publicationAdmission,
        bool isVerified)
    {
        SessionId = sessionId;
        LoadedPath = loadedPath;
        Mapping = mapping;
        BaseSnapshot = baseSnapshot;
        RawWorkspaceSnapshot = rawWorkspaceSnapshot;
        RawWorkspaceRevision = rawWorkspaceRevision;
        ShadowCopyEnabled = shadowCopyEnabled;
        PublicationAdmission = publicationAdmission;
        IsVerified = isVerified;
    }

    public static WorkspaceWriteOperationContext Unverified { get; } = new(
        Guid.Empty,
        loadedPath: null,
        mapping: null,
        baseSnapshot: null,
        rawWorkspaceSnapshot: null,
        rawWorkspaceRevision: -1,
        shadowCopyEnabled: false,
        publicationAdmission: SemanticPublicationAdmission.None,
        isVerified: false);

    public static WorkspaceWriteOperationContext Verified(
        Guid sessionId,
        string? loadedPath,
        AnalyzerShadowMapping? mapping,
        Solution? baseSnapshot,
        Solution? rawWorkspaceSnapshot,
        long rawWorkspaceRevision,
        bool shadowCopyEnabled,
        SemanticPublicationAdmission? publicationAdmission = null)
    {
        return new WorkspaceWriteOperationContext(
            sessionId,
            loadedPath,
            mapping,
            baseSnapshot,
            rawWorkspaceSnapshot,
            rawWorkspaceRevision,
            shadowCopyEnabled,
            publicationAdmission ?? (shadowCopyEnabled
                ? SemanticPublicationAdmission.AllowedMapping
                : SemanticPublicationAdmission.NoOverlay),
            isVerified: true);
    }

    public Guid SessionId { get; }

    public string? LoadedPath { get; }

    public AnalyzerShadowMapping? Mapping { get; }

    public Solution? BaseSnapshot { get; }

    public Solution? RawWorkspaceSnapshot { get; }

    public long RawWorkspaceRevision { get; }

    public bool ShadowCopyEnabled { get; }

    public SemanticPublicationAdmission PublicationAdmission { get; }

    public bool IsVerified { get; }
}

/// <summary>
/// Live session/mapping/raw stamp compared with a held operation context under the write lock.
/// </summary>
internal readonly record struct WorkspaceWriteFreshnessState(
    Guid SessionId,
    string? LoadedPath,
    AnalyzerShadowMapping? Mapping,
    bool ShadowCopyEnabled,
    long RawWorkspaceRevision,
    Solution? RawWorkspaceSnapshot,
    Solution? PublishedSnapshot = null,
    SemanticPublicationAdmission PublicationAdmission = SemanticPublicationAdmission.NoOverlay);

internal readonly record struct WorkspaceWritePreflight(
    bool Accepted,
    string? Reason,
    Solution? CleanedCandidate);
