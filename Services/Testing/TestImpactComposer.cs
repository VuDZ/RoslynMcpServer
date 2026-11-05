using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;

namespace RoslynMcpServer.Services.Testing;

/// <summary>Combines direct current tests, graph-selected tests, and conservative project expansion.</summary>
internal static class TestImpactComposer
{
    /// <summary>
    /// Documents include changed occurrences and the current test-document inventory, including unchanged partial parts.
    /// The caller supplies detected changes only for significant changed paths; unknown baselines use null changes.
    /// Base names come from all current test-project documents, without semantic alias resolution.
    /// </summary>
    internal static TestImpactResult Compose(
        Solution solution,
        ProjectImpactResult projectImpact,
        IReadOnlyList<DocumentImpact> documents,
        IReadOnlyList<AffectedTest> graphTests,
        IReadOnlySet<string> baseTypeNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projectImpact);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(graphTests);
        ArgumentNullException.ThrowIfNull(baseTypeNames);
        cancellationToken.ThrowIfCancellationRequested();

        var tests = DirectTests(documents, cancellationToken);
        foreach (var test in graphTests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddTest(tests, test.ProjectId, test.Key, "Graph reachability.");
            foreach (var reason in test.Reasons)
            {
                AddTest(tests, test.ProjectId, test.Key, reason);
            }
        }

        var expanded = ExpandedProjects(solution, projectImpact, documents, baseTypeNames, cancellationToken);
        return new TestImpactResult(
            tests.Select(pair => new AffectedTest(pair.Key.ProjectId, pair.Key.Key, pair.Value.ToArray())).ToArray(),
            expanded,
            projectImpact.NotCoveredPaths,
            projectImpact.RequiresReload,
            projectImpact.ProjectGraphStale);
    }

    private static Dictionary<(ProjectId ProjectId, BaselineMemberKey Key), HashSet<string>> DirectTests(
        IReadOnlyList<DocumentImpact> documents,
        CancellationToken cancellationToken)
    {
        var tests = new Dictionary<(ProjectId, BaselineMemberKey), HashSet<string>>();
        var currentTests = documents.Where(document => document.Current is not null)
            .SelectMany(document => document.Current!.Members.Where(static member => member.IsTestMethod)
                .Select(member => (document.ProjectId, member.Key))).Distinct().ToArray();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.Current is null)
            {
                continue;
            }
            if (document.Changes is null)
            {
                foreach (var member in document.Current.Members.Where(static member => member.IsTestMethod))
                {
                    AddTest(tests, document.ProjectId, member.Key, "New test or unknown baseline: " + document.Path);
                }
                continue;
            }
            foreach (var change in document.Changes)
            {
                if (change.IsTestMethod && change.Key is not null
                    && document.Current.Members.Any(member => member.Key == change.Key && member.IsTestMethod))
                {
                    AddTest(tests, document.ProjectId, change.Key, "Changed test: " + document.Path);
                }
                if (!change.IsOwnTestClassRoot || change.Key is null)
                {
                    continue;
                }
                var classPath = ClassPath(change.Key);
                foreach (var test in currentTests.Where(test => test.ProjectId == document.ProjectId
                    && test.Key.ContainingTypePath == classPath))
                {
                    AddTest(tests, test.ProjectId, test.Key, "Changed test-class root: " + document.Path);
                }
            }
        }
        return tests;
    }

    private static IReadOnlyDictionary<ProjectId, IReadOnlyList<string>> ExpandedProjects(
        Solution solution,
        ProjectImpactResult projectImpact,
        IReadOnlyList<DocumentImpact> documents,
        IReadOnlySet<string> baseTypeNames,
        CancellationToken cancellationToken)
    {
        var expanded = new Dictionary<ProjectId, HashSet<string>>();
        var testProjects = projectImpact.TestProjectIds.ToHashSet();
        if (projectImpact.ProjectGraphStaleFromGraphFile)
        {
            foreach (var project in projectImpact.TestProjectIds)
            {
                expanded[project] = ["Project graph file changed; reload required."];
            }
        }
        foreach (var path in projectImpact.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var owner in path.ProjectIds)
            {
                var changes = documents.Where(document => document.ProjectId == owner
                    && InputPathCanon.Comparer.Equals(document.Path, path.Path)).ToArray();
                if (changes.Length > 0 && changes.All(document => document.Current is not null
                    && document.Changes is not null && document.Changes.All(change => CanNarrow(change, baseTypeNames))))
                {
                    continue;
                }

                // R-17 (docs/rapid-tia/README.md): suppress only this owner's safe edits, never another project's closure.
                var reason = "Project expansion without a call graph: " + path.Path;
                foreach (var project in ProjectImpactMapper.ExpandProjects(solution, [owner], testProjects))
                {
                    if (!expanded.TryGetValue(project, out var reasons))
                    {
                        reasons = new HashSet<string>(StringComparer.Ordinal);
                        expanded.Add(project, reasons);
                    }
                    reasons.Add(reason);
                }
            }
        }
        return expanded.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray());
    }

    private static bool CanNarrow(BaselineDocumentChange change, IReadOnlySet<string> baseTypeNames)
    {
        if (change.Kind is not (BaselineChangeKind.MemberAdded or BaselineChangeKind.MemberModified)
            || !(change.IsTestMethod || change.IsOwnTestClassRoot) || change.BlocksNarrowing || change.Key is null)
        {
            return false;
        }
        var className = ClassPath(change.Key).Split('.').Last().Split('`')[0];
        return !baseTypeNames.Contains(className);
    }

    private static string ClassPath(BaselineMemberKey key) => key.Kind is SyntaxKind.ClassDeclaration
        or SyntaxKind.StructDeclaration or SyntaxKind.RecordDeclaration or SyntaxKind.RecordStructDeclaration
        or SyntaxKind.InterfaceDeclaration
            ? (key.ContainingTypePath.Length == 0 ? "" : key.ContainingTypePath + ".") + key.Name + "`" + key.Arity
            : key.ContainingTypePath;

    private static void AddTest(
        Dictionary<(ProjectId ProjectId, BaselineMemberKey Key), HashSet<string>> tests,
        ProjectId project,
        BaselineMemberKey key,
        string reason)
    {
        if (!tests.TryGetValue((project, key), out var reasons))
        {
            reasons = new HashSet<string>(StringComparer.Ordinal);
            tests.Add((project, key), reasons);
        }
        reasons.Add(reason);
    }
}
