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
/// This type only touches <see cref="AnalyzerReference"/>s whose load-session provenance confirms an exact
/// consumer reference to one loaded source <see cref="Project"/>. File names and assembly names are not
/// evidence of origin. The source file copied is always the confirmed project's own resolved output
/// (<see cref="Project.CompilationOutputInfo"/>'s <c>AssemblyPath</c>, or <see cref="Project.OutputFilePath"/>),
/// not the (possibly broken) original reference path — that resolved output already has to exist on disk for
/// anything useful to happen.
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
        string? MatchedProjectName,
        string? ShadowCopyPath,
        bool Applied,
        string? SkipReason,
        string? GenerationId = null,
        bool StaleGeneration = false,
        string ReasonCode = AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed,
        AnalyzerReferencePathState OriginalPathState = AnalyzerReferencePathState.NotProvided,
        string? SelectedSourcePath = null,
        AnalyzerReferencePathState SelectedSourcePathState = AnalyzerReferencePathState.NotProvided,
        AnalyzerReferenceSelectionBasis SelectionBasis = AnalyzerReferenceSelectionBasis.None);

    /// <summary>
    /// Test seam: next publisher copies throw <see cref="IOException"/> before writing bytes.
    /// Distinct from <c>SolutionManager.FailNextOverlayPrepare</c>, which skips the copier.
    /// </summary>
    internal static int RemainingForcedCopyFailures
    {
        get => AnalyzerShadowGenerationPublisher.RemainingForcedCopyFailures;
        set => AnalyzerShadowGenerationPublisher.RemainingForcedCopyFailures = value;
    }

    internal static int RemainingForcedAccessFailures
    {
        get => AnalyzerShadowGenerationPublisher.RemainingForcedAccessFailures;
        set => AnalyzerShadowGenerationPublisher.RemainingForcedAccessFailures = value;
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
            loadedPath: null,
            provenanceSnapshot: null);
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
        string? loadedPath,
        AnalyzerProvenanceSnapshot? provenanceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowRootDirectory);
        ArgumentNullException.ThrowIfNull(loader);
        _ = loader;

        var decisions = EnumerateReferenceDecisions(
                solution,
                provenanceSnapshot,
                sessionId)
            .ToList();

        if (decisions.Count == 0)
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
        var entries = new List<AnalyzerShadowReferenceEntry>(decisions.Count);
        var anyPublishFailure = false;
        string? firstFailure = null;

        foreach (var item in decisions)
        {
            if (item.MatchedProject is null)
            {
                entries.Add(CreateSkippedEntry(item, item.Detail));
                continue;
            }

            var sourcePath = item.SelectedSourcePath;
            if (item.SelectedSourcePathState != AnalyzerReferencePathState.Exists)
            {
                var (reasonCode, reason) = item.SelectedSourcePathState switch
                {
                    AnalyzerReferencePathState.AccessFailure => (
                        AnalyzerReferenceReasonCodes.AccessFailure,
                        $"cannot access resolved output of selected project '{item.MatchedProject.Name}'"),
                    AnalyzerReferencePathState.Invalid => (
                        AnalyzerReferenceReasonCodes.PreparationFailure,
                        $"selected project '{item.MatchedProject.Name}' has an invalid resolved output path"),
                    _ => (
                        AnalyzerReferenceReasonCodes.SourceOutputMissing,
                        $"matched project '{item.MatchedProject.Name}' has no existing resolved output file (build it first)"),
                };
                var skipped = KeepPreviousOrSkip(
                    previousMapping,
                    item,
                    reason,
                    reasonCode,
                    item.SelectedSourcePathState);
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

            var sourceFull = Path.GetFullPath(sourcePath!);
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
                var accessFailure = published.FailureReason?.StartsWith(
                    "access-failure:",
                    StringComparison.OrdinalIgnoreCase) == true;
                entries.Add(KeepPreviousOrSkip(
                    previousMapping,
                    item,
                    published.FailureReason ?? "publish-failed",
                    accessFailure
                        ? AnalyzerReferenceReasonCodes.AccessFailure
                        : AnalyzerReferenceReasonCodes.PreparationFailure,
                    accessFailure
                        ? AnalyzerReferencePathState.AccessFailure
                        : item.SelectedSourcePathState));
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
                StaleGeneration: false,
                ReasonCode: AnalyzerReferenceReasonCodes.ReferenceRewritten,
                item.OriginalPathState,
                sourceFull,
                AnalyzerReferencePathState.Exists,
                item.SelectionBasis));
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

    internal static IEnumerable<PendingRewrite> EnumerateInSolutionAnalyzerRefs(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(solution);

        foreach (var decision in EnumerateReferenceDecisions(solution, provenanceSnapshot, sessionId))
        {
            if (decision.MatchedProject is not null
                && decision.ReasonCode == AnalyzerReferenceReasonCodes.ReferenceRewritten)
            {
                yield return new PendingRewrite(
                    decision.Project,
                    decision.AnalyzerReference,
                    decision.MatchedProject);
            }
        }
    }

    internal static IEnumerable<ReferenceDecision> EnumerateReferenceDecisions(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(solution);
        foreach (var project in solution.Projects)
        {
            foreach (var analyzerReference in project.AnalyzerReferences)
            {
                var matchingBindings = string.IsNullOrWhiteSpace(analyzerReference.FullPath)
                    ? []
                    : provenanceSnapshot?.Bindings
                        .Where(binding => binding.ConsumerProjectId == project.Id
                            && AnalyzerShadowMapping.PathsEqual(binding.Identity, analyzerReference.FullPath))
                        .ToArray()
                        ?? [];
                var hasCandidate = HasLoadedSourceCandidate(solution, project.Id, analyzerReference.FullPath);
                if (!hasCandidate && matchingBindings.Length == 0)
                {
                    continue;
                }

                var originalPathState = ProbePath(analyzerReference.FullPath);
                if (provenanceSnapshot is not
                    {
                        Status: AnalyzerProvenanceCaptureStatus.Complete,
                    }
                    || provenanceSnapshot.LoadSessionId != sessionId
                    || string.IsNullOrWhiteSpace(analyzerReference.FullPath))
                {
                    yield return Unconfirmed(
                        project,
                        analyzerReference,
                        originalPathState,
                        "complete load-session provenance is unavailable");
                    continue;
                }

                var bindings = matchingBindings;
                var sourceProjectIds = bindings
                    .Where(binding => binding.Status == AnalyzerProvenanceBindingStatus.Confirmed
                        && binding.SourceProjectId is not null)
                    .Select(binding => binding.SourceProjectId!)
                    .Distinct()
                    .ToArray();
                if (sourceProjectIds.Length > 1
                    || bindings.Any(binding =>
                        binding.Status == AnalyzerProvenanceBindingStatus.AmbiguousSourceProject))
                {
                    yield return new ReferenceDecision(
                        project,
                        analyzerReference,
                        MatchedProject: null,
                        SelectedSourcePath: null,
                        originalPathState,
                        AnalyzerReferencePathState.NotProvided,
                        AnalyzerReferenceReasonCodes.AmbiguousAssemblyName,
                        AnalyzerReferenceSelectionBasis.ProvenanceAmbiguousLoadedSource,
                        "verified provenance did not distinguish one loaded source project");
                    continue;
                }

                if (sourceProjectIds.Length == 1 && sourceProjectIds[0] != project.Id)
                {
                    var matchedProject = solution.GetProject(sourceProjectIds[0]);
                    if (matchedProject is null)
                    {
                        yield return Unconfirmed(
                            project,
                            analyzerReference,
                            originalPathState,
                            "confirmed source project is absent from the loaded solution");
                        continue;
                    }

                    var sourcePath = matchedProject.CompilationOutputInfo.AssemblyPath
                        ?? matchedProject.OutputFilePath;
                    var sourcePathState = ProbePath(sourcePath);
                    yield return new ReferenceDecision(
                        project,
                        analyzerReference,
                        matchedProject,
                        sourcePath,
                        originalPathState,
                        sourcePathState,
                        sourcePathState switch
                        {
                            AnalyzerReferencePathState.Exists => AnalyzerReferenceReasonCodes.ReferenceRewritten,
                            AnalyzerReferencePathState.AccessFailure => AnalyzerReferenceReasonCodes.AccessFailure,
                            AnalyzerReferencePathState.Invalid => AnalyzerReferenceReasonCodes.PreparationFailure,
                            _ => AnalyzerReferenceReasonCodes.SourceOutputMissing,
                        },
                        AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput,
                        Detail: null);
                    continue;
                }

                if (bindings.Any(binding =>
                    binding.Status == AnalyzerProvenanceBindingStatus.MissingSourceProject
                    && !string.IsNullOrWhiteSpace(binding.SourceProjectFile)))
                {
                    yield return new ReferenceDecision(
                        project,
                        analyzerReference,
                        MatchedProject: null,
                        SelectedSourcePath: null,
                        originalPathState,
                        AnalyzerReferencePathState.NotProvided,
                        AnalyzerReferenceReasonCodes.ProvenForeignPath,
                        AnalyzerReferenceSelectionBasis.ProvenanceSourceProjectNotLoaded,
                        "captured analyzer item originates from a project outside the loaded solution");
                    continue;
                }

                yield return Unconfirmed(
                    project,
                    analyzerReference,
                    originalPathState,
                    originalPathState == AnalyzerReferencePathState.AccessFailure
                        ? "original analyzer path could not be inspected"
                        : "no verified source-project binding");
            }
        }
    }

    private static bool HasLoadedSourceCandidate(
        Solution solution,
        ProjectId consumerProjectId,
        string? analyzerPath)
    {
        if (string.IsNullOrWhiteSpace(analyzerPath))
        {
            return false;
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(analyzerPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return solution.Projects.Any(project =>
            project.Id != consumerProjectId
            && (
                string.Equals(
                    Path.GetFileName(project.CompilationOutputInfo.AssemblyPath ?? project.OutputFilePath),
                    fileName,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    string.IsNullOrWhiteSpace(project.AssemblyName)
                        ? null
                        : project.AssemblyName + ".dll",
                    fileName,
                    StringComparison.OrdinalIgnoreCase)));
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
            e.StaleGeneration,
            e.ReasonCode,
            e.OriginalPathState,
            e.SelectedSourcePath,
            e.SelectedSourcePathState,
            e.SelectionBasis)).ToList();
    }

    private static AnalyzerShadowReferenceEntry KeepPreviousOrSkip(
        AnalyzerShadowMapping? previousMapping,
        ReferenceDecision item,
        string reason,
        string reasonCode,
        AnalyzerReferencePathState selectedSourcePathState)
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
                ReasonCode = reasonCode,
                OriginalPathState = item.OriginalPathState,
                SelectedSourcePath = item.SelectedSourcePath,
                SelectedSourcePathState = selectedSourcePathState,
                SelectionBasis = item.SelectionBasis,
            };
        }

        return new AnalyzerShadowReferenceEntry(
            item.Project.Id,
            item.Project.Name,
            item.AnalyzerReference.Display,
            item.AnalyzerReference.FullPath,
            item.MatchedProject?.Name,
            ShadowCopyPath: null,
            GenerationId: null,
            Applied: false,
            SkipReason: reason,
            StaleGeneration: false,
            reasonCode,
            item.OriginalPathState,
            item.SelectedSourcePath,
            selectedSourcePathState,
            item.SelectionBasis);
    }

    private static AnalyzerShadowReferenceEntry CreateSkippedEntry(
        ReferenceDecision item,
        string? detail) =>
        new(
            item.Project.Id,
            item.Project.Name,
            item.AnalyzerReference.Display,
            item.AnalyzerReference.FullPath,
            item.MatchedProject?.Name,
            ShadowCopyPath: null,
            GenerationId: null,
            Applied: false,
            SkipReason: detail,
            StaleGeneration: false,
            item.ReasonCode,
            item.OriginalPathState,
            item.SelectedSourcePath,
            item.SelectedSourcePathState,
            item.SelectionBasis);

    private static ReferenceDecision Unconfirmed(
        Project project,
        AnalyzerReference analyzerReference,
        AnalyzerReferencePathState originalPathState,
        string detail) =>
        new(
            project,
            analyzerReference,
            MatchedProject: null,
            SelectedSourcePath: null,
            originalPathState,
            AnalyzerReferencePathState.NotProvided,
            originalPathState == AnalyzerReferencePathState.AccessFailure
                ? AnalyzerReferenceReasonCodes.AccessFailure
                : AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed,
            AnalyzerReferenceSelectionBasis.None,
            detail);

    /// <summary>Test counter: filesystem path probes. Ordinary publication must not increment this.</summary>
    internal static int PathProbeCount;

    private static AnalyzerReferencePathState ProbePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AnalyzerReferencePathState.NotProvided;
        }

        PathProbeCount++;

        try
        {
            var attributes = File.GetAttributes(Path.GetFullPath(path));
            return (attributes & FileAttributes.Directory) == 0
                ? AnalyzerReferencePathState.Exists
                : AnalyzerReferencePathState.Missing;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return AnalyzerReferencePathState.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return AnalyzerReferencePathState.AccessFailure;
        }
        catch (IOException)
        {
            return AnalyzerReferencePathState.AccessFailure;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return AnalyzerReferencePathState.Invalid;
        }
    }

    internal sealed record ReferenceDecision(
        Project Project,
        AnalyzerReference AnalyzerReference,
        Project? MatchedProject,
        string? SelectedSourcePath,
        AnalyzerReferencePathState OriginalPathState,
        AnalyzerReferencePathState SelectedSourcePathState,
        string ReasonCode,
        AnalyzerReferenceSelectionBasis SelectionBasis,
        string? Detail);

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
