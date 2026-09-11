using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Exact original↔shadow inverse and analyzer-reference classification for the
/// workspace write boundary. Does not infer overlay origin from temp-path prefixes.
/// </summary>
internal static class WorkspaceWriteBoundary
{
    public static WorkspaceWritePreflight Preflight(
        Solution candidate,
        Solution workspaceCurrent,
        WorkspaceWriteOperationContext? operationContext,
        Guid currentSessionId,
        string? currentLoadedPath,
        IAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspaceCurrent);
        ArgumentNullException.ThrowIfNull(loader);

        if (operationContext is not null)
        {
            if (operationContext.SessionId != Guid.Empty
                && operationContext.SessionId != currentSessionId)
            {
                return new WorkspaceWritePreflight(false, "stale-session", null);
            }

            if (!LoadedPathsCompatible(operationContext.LoadedPath, currentLoadedPath))
            {
                return new WorkspaceWritePreflight(false, "stale-session", null);
            }
        }

        var mapping = operationContext?.Mapping;
        var classification = ClassifyAndInvert(candidate, workspaceCurrent, mapping, loader);
        if (!classification.Accepted)
        {
            return classification;
        }

        return classification;
    }

    public static WorkspaceWritePreflight ClassifyAndInvert(
        Solution candidate,
        Solution workspaceCurrent,
        AnalyzerShadowMapping? mapping,
        IAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspaceCurrent);
        ArgumentNullException.ThrowIfNull(loader);

        var cleaned = mapping is null
            ? candidate
            : mapping.InvertKnownReplacements(candidate, workspaceCurrent, loader);

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

    private static bool LoadedPathsCompatible(string? operationPath, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(operationPath) || string.IsNullOrWhiteSpace(currentPath))
        {
            return string.IsNullOrWhiteSpace(operationPath) == string.IsNullOrWhiteSpace(currentPath);
        }

        return AnalyzerShadowMapping.PathsEqual(operationPath, currentPath);
    }
}
