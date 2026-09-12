using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Exact original↔shadow inverse and analyzer-reference classification for the
/// workspace write boundary. Does not infer overlay origin from temp-path prefixes.
/// </summary>
internal static class WorkspaceWriteBoundary
{
    public const string ReasonStaleSession = "stale-session";
    public const string ReasonStaleBase = "stale-base";
    public const string ReasonStalePublication = "stale-publication";
    public const string ReasonUnknownOperationContext = "unknown-operation-context";

    public static WorkspaceWritePreflight Preflight(
        Solution candidate,
        Solution workspaceCurrent,
        WorkspaceWriteOperationContext? operationContext,
        WorkspaceWriteFreshnessState current,
        IAnalyzerAssemblyLoader loader,
        Solution? heldBase = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspaceCurrent);
        ArgumentNullException.ThrowIfNull(loader);

        if (operationContext is null || !operationContext.IsVerified)
        {
            return new WorkspaceWritePreflight(false, ReasonUnknownOperationContext, null);
        }

        if (operationContext.SessionId != Guid.Empty
            && operationContext.SessionId != current.SessionId)
        {
            return new WorkspaceWritePreflight(false, ReasonStaleSession, null);
        }

        if (!LoadedPathsCompatible(operationContext.LoadedPath, current.LoadedPath))
        {
            return new WorkspaceWritePreflight(false, ReasonStaleSession, null);
        }

        if (!RawWorkspaceFresh(operationContext, current, workspaceCurrent)
            || !HeldBaseFresh(heldBase, current, workspaceCurrent))
        {
            return new WorkspaceWritePreflight(false, ReasonStaleBase, null);
        }

        if (!PublicationCompatible(operationContext, current))
        {
            return new WorkspaceWritePreflight(false, ReasonStalePublication, null);
        }

        return ClassifyAndInvert(
            candidate,
            workspaceCurrent,
            operationContext.Mapping,
            loader,
            operationContext.ExcludedReferences);
    }

    public static WorkspaceWritePreflight ClassifyAndInvert(
        Solution candidate,
        Solution workspaceCurrent,
        AnalyzerShadowMapping? mapping,
        IAnalyzerAssemblyLoader loader,
        IReadOnlyList<ExcludedAnalyzerReference>? excludedReferences = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspaceCurrent);
        ArgumentNullException.ThrowIfNull(loader);

        var cleaned = mapping is null
            ? candidate
            : mapping.InvertKnownReplacements(candidate, workspaceCurrent, loader);
        cleaned = SemanticPublicationState.RestoreExcludedReferences(
            cleaned,
            workspaceCurrent,
            excludedReferences);

        foreach (var projectId in cleaned.ProjectIds.ToList())
        {
            var project = cleaned.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var workspaceProject = workspaceCurrent.GetProject(projectId);
            if (workspaceProject is null)
            {
                if (project.AnalyzerReferences.Count > 0)
                {
                    return new WorkspaceWritePreflight(
                        false,
                        "unknown-analyzer-diff: added-project",
                        null);
                }

                continue;
            }

            if (!AnalyzerReferencesEquivalent(project.AnalyzerReferences, workspaceProject.AnalyzerReferences))
            {
                return new WorkspaceWritePreflight(
                    false,
                    "unknown-analyzer-diff",
                    null);
            }
        }

        return new WorkspaceWritePreflight(true, null, cleaned);
    }

    public static bool AnalyzerReferencesEquivalent(
        IReadOnlyList<AnalyzerReference> left,
        IReadOnlyList<AnalyzerReference> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!SameReference(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool SameReference(AnalyzerReference left, AnalyzerReference right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.FullPath is not null
            && right.FullPath is not null
            && AnalyzerShadowMapping.PathsEqual(left.FullPath, right.FullPath))
        {
            return true;
        }

        return string.Equals(left.Display, right.Display, StringComparison.Ordinal)
            && Equals(left.Id, right.Id);
    }

    private static bool HeldBaseFresh(
        Solution? heldBase,
        WorkspaceWriteFreshnessState current,
        Solution workspaceCurrent)
    {
        if (heldBase is null)
        {
            return true;
        }

        var currentRaw = current.RawWorkspaceSnapshot ?? workspaceCurrent;
        return ReferenceEquals(heldBase, currentRaw)
            || ReferenceEquals(heldBase, current.PublishedSnapshot);
    }

    private static bool RawWorkspaceFresh(
        WorkspaceWriteOperationContext operationContext,
        WorkspaceWriteFreshnessState current,
        Solution workspaceCurrent)
    {
        if (operationContext.RawWorkspaceRevision != current.RawWorkspaceRevision)
        {
            return false;
        }

        var currentRaw = current.RawWorkspaceSnapshot ?? workspaceCurrent;
        return operationContext.RawWorkspaceSnapshot is not null
            && ReferenceEquals(operationContext.RawWorkspaceSnapshot, currentRaw);
    }

    private static bool PublicationCompatible(
        WorkspaceWriteOperationContext operationContext,
        WorkspaceWriteFreshnessState current)
    {
        return operationContext.ShadowCopyEnabled == current.ShadowCopyEnabled
            && operationContext.PublicationAdmission == current.PublicationAdmission
            && ReferenceEquals(operationContext.Mapping, current.Mapping)
            && ExclusionsEqual(
                operationContext.ExcludedReferences,
                current.ExcludedReferences ?? Array.Empty<ExcludedAnalyzerReference>());
    }

    private static bool ExclusionsEqual(
        IReadOnlyList<ExcludedAnalyzerReference> left,
        IReadOnlyList<ExcludedAnalyzerReference> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].ProjectId != right[i].ProjectId
                || !Equals(left[i].AnalyzerId, right[i].AnalyzerId))
            {
                return false;
            }

            var leftPath = left[i].FullPath;
            var rightPath = right[i].FullPath;
            if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
            {
                if (leftPath != rightPath)
                {
                    return false;
                }

                continue;
            }

            if (!AnalyzerShadowMapping.PathsEqual(leftPath, rightPath))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LoadedPathsCompatible(string? operationPath, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(operationPath) || string.IsNullOrWhiteSpace(currentPath))
        {
            return string.IsNullOrWhiteSpace(operationPath) == string.IsNullOrWhiteSpace(currentPath);
        }

        return AnalyzerShadowMapping.PathsEqual(operationPath, currentPath);
    }
}
