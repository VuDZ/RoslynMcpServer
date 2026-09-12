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

    public SemanticPublicationAdmission Admission { get; }

    public string? BanReason { get; }

    public IReadOnlyList<ExcludedAnalyzerReference> ExcludedReferences { get; }

    public bool AllowsRawReferences => Admission == SemanticPublicationAdmission.NoOverlay;

    public bool AllowsOverlay => Admission == SemanticPublicationAdmission.AllowedMapping;

    public bool IsBanned => Admission == SemanticPublicationAdmission.Banned;

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
}
