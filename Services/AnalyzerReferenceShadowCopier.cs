using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Rewrites <see cref="AnalyzerReference"/>s that point at another project's build output <b>inside the same
/// solution</b> so they load from a private shadow-copy folder instead of the analyzer/generator project's real
/// output path. Fixes two related issues confirmed via an external repro (see
/// <c>docs/ARCHITECTURE.md</c> and <c>ProjectOutputDiagnosticsLogger</c>) when a repo-wide
/// <c>Directory.Build.props</c> overrides <c>OutputPath</c> for a project referenced with
/// <c>OutputItemType="Analyzer"</c>:
/// <list type="number">
/// <item>MSBuildWorkspace's design-time-resolved <see cref="AnalyzerReference.FullPath"/> for that
/// <c>ProjectReference</c> can disagree with the referenced project's own resolved
/// <see cref="Project.CompilationOutputInfo"/>/<see cref="Project.OutputFilePath"/>, pointing at a path that does
/// not exist and silently disabling source generation for the referencing project.</item>
/// <item>Even when the path is correct, loading the analyzer assembly directly from its real build output keeps
/// that file locked for the life of the MCP process, which then breaks a later <c>dotnet build</c> of the
/// analyzer/generator project (MSB3027).</item>
/// </list>
/// This type only touches <see cref="AnalyzerReference"/>s whose file name (without extension) matches another
/// project's <see cref="Project.AssemblyName"/> in the same <see cref="Solution"/>; ambiguous assembly names
/// (two projects sharing one) are left untouched rather than guessed. The source file copied is always the
/// matched project's own resolved output (<see cref="Project.CompilationOutputInfo"/>'s <c>AssemblyPath</c>, or
/// <see cref="Project.OutputFilePath"/>), not the (possibly broken) original reference path — that resolved
/// output already has to exist on disk for anything useful to happen.
/// File preparation (immutable content-hashed generations) is separate from the pure <see cref="Solution"/>
/// transform. The caller (<see cref="SolutionManager"/>) must never apply the result via
/// <see cref="Workspace.TryApplyChanges(Solution)"/> against the real <see cref="MSBuildWorkspace"/> — see
/// <see cref="SolutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync"/> for why (it persists analyzer
/// reference changes back to the <c>.csproj</c> on disk).
/// </summary>
public static class AnalyzerReferenceShadowCopier
{
    public sealed record RewriteResult(
        string ProjectName,
        string AnalyzerDisplay,
        string? OriginalFullPath,
        string MatchedProjectName,
        string? ShadowCopyPath,
        bool Applied,
        string? SkipReason,
        string? GenerationId = null,
        bool StaleGeneration = false);

    /// <summary>
    /// Test seam: next publisher copies throw <see cref="IOException"/> before writing bytes.
    /// Distinct from <c>SolutionManager.FailNextOverlayPrepare</c>, which skips the copier.
    /// </summary>
    internal static int RemainingForcedCopyFailures
    {
        get => AnalyzerShadowGenerationPublisher.RemainingForcedCopyFailures;
        set => AnalyzerShadowGenerationPublisher.RemainingForcedCopyFailures = value;
    }

    /// <summary>
    /// Computes a stable, human-readable shadow-copy root directory for a loaded solution/project path, under
    /// the OS temp directory. Distinct loaded paths never collide; the same path always maps to the same
    /// directory so repeated loads can reuse published generations (never overwrite them).
    /// </summary>
    public static string GetDefaultShadowRootDirectory(string loadedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loadedPath);

        var normalized = Path.GetFullPath(loadedPath).ToUpperInvariant();
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))[..8];
        var name = Path.GetFileNameWithoutExtension(loadedPath);
        return Path.Combine(Path.GetTempPath(), "RoslynMcpServer.AnalyzerShadowCopy", $"{name}_{hash}");
    }

    /// <summary>
    /// Prepares immutable generations then rewrites in-solution analyzer references. Combined helper for
    /// unit tests; production load/refresh uses <see cref="PrepareInSolutionAnalyzerReferences"/> and
    /// <see cref="ApplyMapping"/> separately so document edit can reapply without analyzer file I/O.
    /// </summary>
    public static (Solution Solution, IReadOnlyList<RewriteResult> Results) ShadowCopyInSolutionAnalyzerReferences(
        Solution solution,
        string shadowRootDirectory,
        IAnalyzerAssemblyLoader loader)
    {
        var prepared = PrepareInSolutionAnalyzerReferences(
            solution,
            shadowRootDirectory,
            loader,
            previousMapping: null,
            sessionId: Guid.Empty,
            loadedPath: null);
        var rewritten = prepared.Mapping.HasAnyApplied
            ? prepared.Mapping.Apply(solution, loader)
            : solution;
        return (rewritten, prepared.Results);
    }

    internal static AnalyzerShadowPrepareOutcome PrepareInSolutionAnalyzerReferences(
        Solution solution,
        string shadowRootDirectory,
        IAnalyzerAssemblyLoader loader,
        AnalyzerShadowMapping? previousMapping,
        Guid sessionId,
        string? loadedPath)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowRootDirectory);
        ArgumentNullException.ThrowIfNull(loader);
        _ = loader;

        var workItems = EnumerateInSolutionAnalyzerRefs(solution).ToList();

        if (workItems.Count == 0)
        {
            var empty = new AnalyzerShadowMapping(sessionId, loadedPath, Array.Empty<AnalyzerShadowReferenceEntry>());
            return new AnalyzerShadowPrepareOutcome(
                empty,
                Array.Empty<RewriteResult>(),
                RefreshSucceeded: true,
                UsedPreviousMappingAsStale: false,
                FailureSummary: null);
        }

        var publishBySource = new Dictionary<string, AnalyzerShadowPublishResult>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<AnalyzerShadowReferenceEntry>(workItems.Count);
        var anyPublishFailure = false;
        string? firstFailure = null;

        foreach (var item in workItems)
        {
            var sourcePath = item.MatchedProject.CompilationOutputInfo.AssemblyPath ?? item.MatchedProject.OutputFilePath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                var skipped = KeepPreviousOrSkip(
                    previousMapping,
                    item,
                    $"matched project '{item.MatchedProject.Name}' has no existing resolved output file (build it first)");
                if (!skipped.Applied)
                {
                    anyPublishFailure = true;
                    firstFailure ??= skipped.SkipReason;
                }
                else if (skipped.StaleGeneration)
                {
                    anyPublishFailure = true;
                    firstFailure ??= skipped.SkipReason;
                }

                entries.Add(skipped);
                continue;
            }

            var sourceFull = Path.GetFullPath(sourcePath);
            if (!publishBySource.TryGetValue(sourceFull, out var published))
            {
                published = AnalyzerShadowGenerationPublisher.PublishMainOnly(
                    sourceFull,
                    shadowRootDirectory,
                    item.MatchedProject.Name);
                publishBySource[sourceFull] = published;
            }

            if (!published.Success || string.IsNullOrWhiteSpace(published.MainShadowPath))
            {
                anyPublishFailure = true;
                firstFailure ??= published.FailureReason;
                entries.Add(KeepPreviousOrSkip(
                    previousMapping,
                    item,
                    published.FailureReason ?? "publish-failed"));
                continue;
            }

            entries.Add(new AnalyzerShadowReferenceEntry(
                item.Project.Id,
                item.Project.Name,
                item.AnalyzerReference.Display,
                item.AnalyzerReference.FullPath,
                item.MatchedProject.Name,
                published.MainShadowPath,
                published.GenerationId,
                Applied: true,
                SkipReason: null,
                StaleGeneration: false));
        }

        var mapping = new AnalyzerShadowMapping(sessionId, loadedPath, entries);
        if (anyPublishFailure && previousMapping is { HasAnyApplied: true } && !mapping.HasAnyApplied)
        {
            var stale = previousMapping.WithStale(firstFailure ?? "refresh-failed");
            return new AnalyzerShadowPrepareOutcome(
                stale,
                ToRewriteResults(stale),
                RefreshSucceeded: false,
                UsedPreviousMappingAsStale: true,
                FailureSummary: firstFailure);
        }

        return new AnalyzerShadowPrepareOutcome(
            mapping,
            ToRewriteResults(mapping),
            RefreshSucceeded: !anyPublishFailure,
            UsedPreviousMappingAsStale: mapping.Entries.Any(e => e.StaleGeneration),
            FailureSummary: firstFailure);
    }

    internal static IEnumerable<PendingRewrite> EnumerateInSolutionAnalyzerRefs(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var assemblyNameToProjectId = solution.Projects
            .GroupBy(p => p.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            foreach (var analyzerReference in project.AnalyzerReferences)
            {
                var fileName = TryGetFileNameWithoutExtension(analyzerReference.FullPath);
                if (fileName is null
                    || !assemblyNameToProjectId.TryGetValue(fileName, out var matchedProjectId)
                    || matchedProjectId == project.Id)
                {
                    continue;
                }

                var matchedProject = solution.GetProject(matchedProjectId);
                if (matchedProject is null)
                {
                    continue;
                }

                yield return new PendingRewrite(project, analyzerReference, matchedProject);
            }
        }
    }

    internal static Solution ApplyMapping(
        Solution solution,
        AnalyzerShadowMapping mapping,
        IAnalyzerAssemblyLoader loader)
    {
        return mapping.Apply(solution, loader);
    }

    internal static IReadOnlyList<RewriteResult> ToRewriteResults(AnalyzerShadowMapping mapping)
    {
        return mapping.Entries.Select(e => new RewriteResult(
            e.ProjectName,
            e.AnalyzerDisplay,
            e.OriginalFullPath,
            e.MatchedProjectName,
            e.ShadowCopyPath,
            e.Applied,
            e.SkipReason,
            e.GenerationId,
            e.StaleGeneration)).ToList();
    }

    private static AnalyzerShadowReferenceEntry KeepPreviousOrSkip(
        AnalyzerShadowMapping? previousMapping,
        PendingRewrite item,
        string reason)
    {
        var previous = previousMapping?.Entries.FirstOrDefault(e =>
            e.ProjectId == item.Project.Id
            && string.Equals(e.OriginalFullPath, item.AnalyzerReference.FullPath, StringComparison.OrdinalIgnoreCase)
            && e.Applied
            && !string.IsNullOrWhiteSpace(e.ShadowCopyPath));
        if (previous is not null)
        {
            return previous with
            {
                StaleGeneration = true,
                SkipReason = "stale-generation: " + reason,
            };
        }

        return new AnalyzerShadowReferenceEntry(
            item.Project.Id,
            item.Project.Name,
            item.AnalyzerReference.Display,
            item.AnalyzerReference.FullPath,
            item.MatchedProject.Name,
            ShadowCopyPath: null,
            GenerationId: null,
            Applied: false,
            SkipReason: reason,
            StaleGeneration: false);
    }

    private static string? TryGetFileNameWithoutExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal readonly record struct PendingRewrite(
        Project Project,
        AnalyzerReference AnalyzerReference,
        Project MatchedProject);
}

internal readonly record struct AnalyzerShadowPrepareOutcome(
    AnalyzerShadowMapping Mapping,
    IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> Results,
    bool RefreshSucceeded,
    bool UsedPreviousMappingAsStale,
    string? FailureSummary);
