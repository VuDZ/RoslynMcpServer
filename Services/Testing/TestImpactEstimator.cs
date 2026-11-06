using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing;

internal static class TestImpactEstimator
{
    internal static async Task<TestImpactEstimate> EstimateAsync(
        Solution solution,
        WorkspaceInputMap inputMap,
        BaselineSessionSnapshot baseline,
        string? workspacePath,
        bool projectGraphStale,
        bool projectGraphStaleFromGraphFile,
        CancellationToken cancellationToken)
    {
        var changedPaths = baseline.TouchedPaths.ToHashSet(InputPathCanon.Comparer);
        var notCovered = new List<string>();
        var documents = await DocumentsAsync(solution, inputMap, baseline, changedPaths, notCovered, cancellationToken).ConfigureAwait(false);
        var mapped = await ProjectImpactMapper.MapAsync(solution, inputMap, changedPaths, workspacePath,
            projectGraphStale, projectGraphStaleFromGraphFile, cancellationToken).ConfigureAwait(false);
        await AddUnloadedDocumentsAsync(solution, mapped, documents, notCovered, cancellationToken).ConfigureAwait(false);
        var baseNames = baseline.Documents.Where(state => state.Snapshot is not null)
            .SelectMany(state => state.Snapshot!.BaseTypeNames)
            .Concat(documents.Where(document => document.Current is not null).SelectMany(document => document.Current!.BaseTypeNames))
            .ToHashSet(StringComparer.Ordinal);
        var impact = TestImpactComposer.Compose(solution, mapped, documents, [], baseNames, cancellationToken);
        var classes = await VstestTestInventory.BuildAsync(solution, cancellationToken).ConfigureAwait(false);
        if (baseline.HasObservationGap)
        {
            var expanded = impact.ExpandedTestProjects.ToDictionary(pair => pair.Key, pair => pair.Value);
            var testProjects = await ProjectImpactMapper.FindTestProjectsAsync(solution, cancellationToken).ConfigureAwait(false);
            foreach (var project in testProjects.Concat(classes.Select(testClass => testClass.ProjectId)).Distinct())
            {
                expanded[project] = expanded.GetValueOrDefault(project, [])
                    .Append("Observation gap: non-C# inputs may have changed; conservative whole-project expansion.").ToArray();
            }
            impact = impact with { ExpandedTestProjects = expanded };
            notCovered.Add("Observation gap: non-C# input history is incomplete and the project graph may be stale. "
                + "This limitation persists for this baseline, including after reload.");
        }
        else if (baseline.RequiresFullScan)
        {
            // Ordinary reload requires C# rescanning without permanent unrelated-project expansion.
            // See docs/rapid-tia/epoch-1-project-level/evidence/task-08-review-round-2-claude.md (R2-001).
            notCovered.Add("Reload/reopen observation window: non-C# changes may have been missed. "
                + "C# sources are rescanned; this non-C# limitation persists for this baseline.");
        }
        notCovered.AddRange(mapped.NotCoveredPaths.Select(path => "Path outside the loaded input map: " + path));
        if (impact.ProjectGraphStale)
        {
            notCovered.Add("Project graph stale; reload required to refresh membership and project references.");
        }
        var errors = await CompilationErrorsAsync(solution, changedPaths, cancellationToken).ConfigureAwait(false);
        return new TestImpactEstimate(documents, mapped.Paths.Select(path => path.Path).ToArray(), impact,
            VstestRunPlanner.Plan(impact, classes, cancellationToken), classes, notCovered.Distinct().ToArray(), errors);
    }

    private static async Task<List<DocumentImpact>> DocumentsAsync(
        Solution solution,
        WorkspaceInputMap inputMap,
        BaselineSessionSnapshot baseline,
        HashSet<string> changedPaths,
        List<string> notCovered,
        CancellationToken cancellationToken)
    {
        var documents = new List<DocumentImpact>();
        var before = baseline.Documents.ToDictionary(state => state.Key);
        var originalPaths = baseline.Documents.Select(state => state.Key.DocumentPath).ToHashSet(InputPathCanon.Comparer);
        var inventory = BaselineSession.Inventory(solution);
        var comparedPaths = originalPaths
            .Concat(inventory.Select(entry => entry.Key.DocumentPath)).ToHashSet(InputPathCanon.Comparer);
        changedPaths.ExceptWith(comparedPaths);
        // A refreshed map can lose an original AdditionalFile; only proven new-source tombstones are removable.
        // See docs/rapid-tia/epoch-1-project-level/evidence/task-08-review-round-3.md (R3-001).
        changedPaths.RemoveWhere(path => WorkspaceDiskPathFilter.IsCSharpSource(path)
            && baseline.LoadedSourcePaths.Contains(path) && !originalPaths.Contains(path)
            && !baseline.OriginalNonSourcePaths.Contains(path) && !File.Exists(path) && inputMap.Find(path) is null);
        foreach (var entry in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = solution.GetDocument(entry.Id)!;
            var isNewSourcePath = !originalPaths.Contains(document.FilePath!)
                && !baseline.OriginalNonSourcePaths.Contains(document.FilePath!);
            before.Remove(entry.Key, out var previous);
            if (previous is not null && previous.Status != BaselineDocumentStatus.Known)
            {
                notCovered.Add("Baseline unknown: " + document.FilePath);
            }
            // Disk sync retains cached text for a missing file; deletion must not compare that text as unchanged.
            if (!File.Exists(document.FilePath))
            {
                if (isNewSourcePath
                    && inputMap.Find(document.FilePath!)?.Occurrences.All(occurrence => occurrence.Role == InputRole.UserInput) != false)
                {
                    continue;
                }
                changedPaths.Add(document.FilePath!);
                var removed = previous?.Snapshot?.Members.Select(member => new BaselineDocumentChange(
                    BaselineChangeKind.MemberRemoved, BaselineChangeReason.MemberSyntax, member.Key)).ToArray();
                documents.Add(new DocumentImpact(entry.Id.ProjectId, document.FilePath!, null, removed));
                continue;
            }
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is null)
            {
                changedPaths.Add(entry.Key.DocumentPath);
                notCovered.Add("Current source could not be read: " + document.FilePath);
                documents.Add(new DocumentImpact(entry.Id.ProjectId, document.FilePath!, null, null));
                continue;
            }
            var current = BaselineDocumentSnapshotBuilder.Build(tree);
            IReadOnlyList<BaselineDocumentChange>? changes = previous is { Status: BaselineDocumentStatus.Known, Snapshot: { } snapshot }
                ? BaselineDocumentChangeDetector.Compare(snapshot, current) : null;
            if (previous is null && isNewSourcePath)
            {
                // Original path absence proves addition; ordinary imports/namespaces affect only this new file.
                // Global usings, assembly attributes, directives and binding-sensitive members still widen normally.
                var empty = BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(""))
                    with { ImportsAndNamespacesHash = current.ImportsAndNamespacesHash };
                changes = BaselineDocumentChangeDetector.Compare(empty, current);
            }
            else if (previous is null)
            {
                notCovered.Add("Baseline unknown for this document context: " + document.FilePath);
            }
            documents.Add(new DocumentImpact(entry.Id.ProjectId, document.FilePath!, current, changes));
            if (changes is null || changes.Count > 0)
            {
                changedPaths.Add(document.FilePath!);
            }
        }
        foreach (var previous in before.Values)
        {
            changedPaths.Add(previous.Key.DocumentPath);
            if (previous.Status != BaselineDocumentStatus.Known)
            {
                notCovered.Add("Baseline unknown: " + previous.Key.DocumentPath);
            }
            foreach (var project in solution.Projects.Where(project =>
                InputPathCanon.Comparer.Equals(project.FilePath, previous.Key.ProjectPath)))
            {
                var removed = previous.Snapshot?.Members.Select(member => new BaselineDocumentChange(
                    BaselineChangeKind.MemberRemoved, BaselineChangeReason.MemberSyntax, member.Key)).ToArray();
                documents.Add(new DocumentImpact(project.Id, previous.Key.DocumentPath, null, removed));
            }
        }
        return documents;
    }

    private static async Task AddUnloadedDocumentsAsync(
        Solution solution,
        ProjectImpactResult mapped,
        List<DocumentImpact> documents,
        List<string> notCovered,
        CancellationToken cancellationToken)
    {
        foreach (var path in mapped.Paths.Where(path => WorkspaceDiskPathFilter.IsCSharpSource(path.Path)
            && !documents.Any(document => InputPathCanon.Comparer.Equals(document.Path, path.Path))))
        {
            if (!File.Exists(path.Path))
            {
                continue;
            }
            try
            {
                var source = await File.ReadAllTextAsync(path.Path, cancellationToken).ConfigureAwait(false);
                foreach (var projectId in path.ProjectIds)
                {
                    var options = solution.GetProject(projectId)?.ParseOptions as CSharpParseOptions;
                    var tree = CSharpSyntaxTree.ParseText(source, options, path.Path, cancellationToken: cancellationToken);
                    documents.Add(new DocumentImpact(projectId, path.Path, BaselineDocumentSnapshotBuilder.Build(tree), null));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notCovered.Add("Current source could not be read: " + path.Path);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> CompilationErrorsAsync(
        Solution solution, IReadOnlySet<string> changedPaths, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var project in solution.Projects.Where(project => project.Documents.Any(document =>
            document.FilePath is not null && changedPaths.Contains(document.FilePath))))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }
            errors.AddRange(compilation.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Location.SourceTree is { } tree && changedPaths.Contains(tree.FilePath)).Select(diagnostic => diagnostic.ToString()));
        }
        return errors.Distinct().ToArray();
    }
}
