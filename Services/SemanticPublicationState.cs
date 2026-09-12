using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Session-scoped semantic publication admission. Distinct from the last
/// refresh or execution observation: a later "suitable" real DLL does not
/// lift a failed opt-in ban.
/// </summary>
internal enum SemanticPublicationAdmission
{
    None = 0,
    NoOverlay = 1,
    AllowedMapping = 2,
    Banned = 3,
    Unavailable = 4,
}

internal readonly record struct ExcludedAnalyzerReference(
    ProjectId ProjectId,
    string? FullPath,
    object? AnalyzerId);

/// <summary>
/// Persistent admission plus the predetermined excluded-reference set.
/// Ordinary publication applies this set; it does not rediscover provenance
/// or probe analyzer files.
/// </summary>
internal sealed class SemanticPublicationState
{
    public static SemanticPublicationState None { get; } = new(
        SemanticPublicationAdmission.None,
        banReason: null,
        Array.Empty<ExcludedAnalyzerReference>());

    public static SemanticPublicationState NoOverlay { get; } = new(
        SemanticPublicationAdmission.NoOverlay,
        banReason: null,
        Array.Empty<ExcludedAnalyzerReference>());

    public static SemanticPublicationState AllowedMapping { get; } = new(
        SemanticPublicationAdmission.AllowedMapping,
        banReason: null,
        Array.Empty<ExcludedAnalyzerReference>());

    public static SemanticPublicationState Allow(IReadOnlyList<ExcludedAnalyzerReference>? excluded)
    {
        if (excluded is null || excluded.Count == 0)
        {
            return AllowedMapping;
        }

        return new(
            SemanticPublicationAdmission.AllowedMapping,
            banReason: null,
            excluded);
    }

    private SemanticPublicationState(
        SemanticPublicationAdmission admission,
        string? banReason,
        IReadOnlyList<ExcludedAnalyzerReference> excludedReferences)
    {
        Admission = admission;
        BanReason = banReason;
        ExcludedReferences = excludedReferences;
    }

    public static SemanticPublicationState Banned(
        string? reason,
        IReadOnlyList<ExcludedAnalyzerReference> excluded)
    {
        return new(
            SemanticPublicationAdmission.Banned,
            reason,
            excluded ?? Array.Empty<ExcludedAnalyzerReference>());
    }

    public static SemanticPublicationState Unavailable(string? reason)
    {
        return new(
            SemanticPublicationAdmission.Unavailable,
            reason,
            Array.Empty<ExcludedAnalyzerReference>());
    }

    public SemanticPublicationAdmission Admission { get; }

    public string? BanReason { get; }

    public IReadOnlyList<ExcludedAnalyzerReference> ExcludedReferences { get; }

    public bool AllowsRawReferences => Admission == SemanticPublicationAdmission.NoOverlay;

    public bool AllowsOverlay => Admission == SemanticPublicationAdmission.AllowedMapping;

    public bool IsBanned => Admission == SemanticPublicationAdmission.Banned;

    public bool IsUnavailable => Admission == SemanticPublicationAdmission.Unavailable;

    public bool WithholdsSnapshot =>
        Admission is SemanticPublicationAdmission.None or SemanticPublicationAdmission.Unavailable;

    public static Solution ApplyExcludedReferences(
        Solution solution,
        IReadOnlyList<ExcludedAnalyzerReference> excluded)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (excluded is null || excluded.Count == 0)
        {
            return solution;
        }

        var byProject = excluded
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var (projectId, blocked) in byProject)
        {
            var project = solution.GetProject(projectId);
            if (project is null || project.AnalyzerReferences.Count == 0)
            {
                continue;
            }

            var kept = new List<AnalyzerReference>(project.AnalyzerReferences.Count);
            var changed = false;
            foreach (var reference in project.AnalyzerReferences)
            {
                if (IsExcluded(reference, blocked))
                {
                    changed = true;
                    continue;
                }

                kept.Add(reference);
            }

            if (changed)
            {
                solution = solution.WithProjectAnalyzerReferences(projectId, kept);
            }
        }

        return solution;
    }

    /// <summary>
    /// Restores predetermined excluded references from the raw workspace so exact
    /// inverse does not treat a fail-closed strip as a <c>.csproj</c> deletion.
    /// Unknown analyzer diffs are not inserted.
    /// </summary>
    public static Solution RestoreExcludedReferences(
        Solution candidate,
        Solution workspaceCurrent,
        IReadOnlyList<ExcludedAnalyzerReference>? excluded)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workspaceCurrent);
        if (excluded is null || excluded.Count == 0)
        {
            return candidate;
        }

        var byProject = excluded
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var (projectId, blocked) in byProject)
        {
            var workspaceProject = workspaceCurrent.GetProject(projectId);
            var candidateProject = candidate.GetProject(projectId);
            if (workspaceProject is null || candidateProject is null)
            {
                continue;
            }

            var result = candidateProject.AnalyzerReferences.ToList();
            var workspaceRefs = workspaceProject.AnalyzerReferences;
            var changed = false;
            for (var i = 0; i < workspaceRefs.Count; i++)
            {
                var workspaceRef = workspaceRefs[i];
                if (!IsExcluded(workspaceRef, blocked))
                {
                    continue;
                }

                if (result.Any(existing => SameIdentity(existing, workspaceRef)))
                {
                    continue;
                }

                result.Insert(Math.Min(i, result.Count), workspaceRef);
                changed = true;
            }

            if (changed)
            {
                candidate = candidate.WithProjectAnalyzerReferences(projectId, result);
            }
        }

        return candidate;
    }

    public static IReadOnlyList<ExcludedAnalyzerReference> CaptureFromInSolutionReferences(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(solution);
        return AnalyzerReferenceShadowCopier
            .EnumerateInSolutionAnalyzerRefs(solution, provenanceSnapshot, sessionId)
            .Select(item => new ExcludedAnalyzerReference(
                item.Project.Id,
                item.AnalyzerReference.FullPath,
                item.AnalyzerReference.Id))
            .ToArray();
    }

    private static bool IsExcluded(
        AnalyzerReference reference,
        IReadOnlyList<ExcludedAnalyzerReference> blocked)
    {
        foreach (var excluded in blocked)
        {
            if (!string.IsNullOrWhiteSpace(reference.FullPath)
                && !string.IsNullOrWhiteSpace(excluded.FullPath)
                && AnalyzerShadowMapping.PathsEqual(reference.FullPath, excluded.FullPath))
            {
                return true;
            }

            if (excluded.AnalyzerId is not null && Equals(reference.Id, excluded.AnalyzerId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameIdentity(AnalyzerReference left, AnalyzerReference right)
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

        return Equals(left.Id, right.Id);
    }
}
