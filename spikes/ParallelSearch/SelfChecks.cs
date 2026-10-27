using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ParallelSearch;

/// <summary>Behavioral checks of <see cref="DeclarationSearch"/> on in-memory solutions; no MSBuild.</summary>
internal static class SelfChecks
{
    public static async Task RunAsync()
    {
        await SameAnswerForEveryDegreeAsync().ConfigureAwait(false);
        await ExternalCancellationStopsEveryProjectAsync().ConfigureAwait(false);
        await FirstFailureIsRethrownAndStopsTheRestAsync().ConfigureAwait(false);
        await EmptySolutionAndProjectWithoutDocumentsAsync().ConfigureAwait(false);
    }

    private static async Task SameAnswerForEveryDegreeAsync()
    {
        string? expected = null;
        foreach (var degree in new[] { 1, 2, 4, 8 })
        {
            // A new workspace per degree: no declaration index built for an earlier degree is reused.
            using var workspace = new AdhocWorkspace();
            var outcome = await DeclarationSearch.SearchAsync(
                    SolutionWithWidgets(workspace), "Widget", SymbolFilter.Type | SymbolFilter.Member, degree, CancellationToken.None)
                .ConfigureAwait(false);
            var answer = string.Join('\n', SearchMeasurement.DescribeResult(outcome, "/fixture"));
            // Projects 0, 3, 6 and 9 declare class Widget and its method widget; the search ignores case.
            Check(outcome.Symbols.Count == 8, $"degree {degree}: expected 8 declarations, got {outcome.Symbols.Count}");
            Check(outcome.Rows.Count == 12, $"degree {degree}: expected 12 project rows, got {outcome.Rows.Count}");
            expected ??= answer;
            Check(answer == expected, $"degree {degree}: answer differs from degree 1");
        }
    }

    private static Solution SolutionWithWidgets(AdhocWorkspace workspace)
    {
        var solution = workspace.CurrentSolution;
        for (var project = 0; project < 12; project++)
        {
            var id = ProjectId.CreateNewId();
            solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "P" + project, "P" + project, LanguageNames.CSharp));
            for (var document = 0; document < project % 4 + 1; document++)
            {
                var text = project % 3 == 0 && document == 0
                    ? $"namespace N{project}; class Widget {{ void widget() {{ }} }}"
                    : $"namespace N{project}; class Other{document} {{ }}";
                solution = solution.AddDocument(DocumentInfo.Create(
                    DocumentId.CreateNewId(id),
                    $"D{document}.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                    filePath: $"/fixture/P{project}/D{document}.cs"));
            }
        }

        return solution;
    }

    private static async Task ExternalCancellationStopsEveryProjectAsync()
    {
        var solution = SolutionWithBlockingProjects(projects: 6, failing: null);
        foreach (var degree in new[] { 1, 4 })
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();
            var thrown = await CaptureAsync(() => DeclarationSearch.SearchAsync(
                solution, "Widget", SymbolFilter.Type, degree, cancellation.Token)).ConfigureAwait(false);
            Check(thrown is OperationCanceledException, $"degree {degree}: expected cancellation, got {thrown?.GetType().Name ?? "no exception"}");
            Check(watch.Elapsed < TimeSpan.FromSeconds(10), $"degree {degree}: cancellation took {watch.Elapsed}");
        }
    }

    private static async Task FirstFailureIsRethrownAndStopsTheRestAsync()
    {
        var solution = SolutionWithBlockingProjects(projects: 6, failing: 2);
        foreach (var degree in new[] { 1, 4 })
        {
            var watch = Stopwatch.StartNew();
            var thrown = await CaptureAsync(() => DeclarationSearch.SearchAsync(
                solution, "Widget", SymbolFilter.Type, degree, CancellationToken.None)).ConfigureAwait(false);
            Check(thrown is InvalidOperationException { Message: "unreadable project 2" },
                $"degree {degree}: expected the original failure, got {thrown?.GetType().Name ?? "no exception"}: {thrown?.Message}");
            Check(watch.Elapsed < TimeSpan.FromSeconds(10), $"degree {degree}: failure took {watch.Elapsed}");
        }
    }

    private static async Task EmptySolutionAndProjectWithoutDocumentsAsync()
    {
        using var workspace = new AdhocWorkspace();
        var empty = await DeclarationSearch.SearchAsync(workspace.CurrentSolution, "Widget", SymbolFilter.Type, 4, CancellationToken.None)
            .ConfigureAwait(false);
        Check(empty.Symbols.Count == 0 && empty.Rows.Count == 0, "empty solution: expected no declarations and no rows");

        var id = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Empty", "Empty", LanguageNames.CSharp));
        var bare = await DeclarationSearch.SearchAsync(solution, "Widget", SymbolFilter.Type, 4, CancellationToken.None)
            .ConfigureAwait(false);
        Check(bare.Symbols.Count == 0, "project without documents: expected no declarations");
        Check(bare.Rows.Count == 1 && bare.Rows[0] is { Outcome: "success", Matches: 0, Documents: 0 },
            "project without documents: expected one successful row with zero matches");
    }

    /// <summary>
    /// Projects whose only document does not finish loading until the search is cancelled. With
    /// <paramref name="failing"/>, the projects before it load at once and that project fails: a degree
    /// above 1 has already started blocked projects after it and must cancel them to finish.
    /// </summary>
    private static Solution SolutionWithBlockingProjects(int projects, int? failing)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        for (var project = 0; project < projects; project++)
        {
            var id = ProjectId.CreateNewId();
            solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "B" + project, "B" + project, LanguageNames.CSharp));
            TextLoader loader = failing is not null && project < failing
                ? TextLoader.From(TextAndVersion.Create(SourceText.From("class Fast { }"), VersionStamp.Create()))
                : project == failing
                    ? new FailingLoader($"unreadable project {project}")
                    : new BlockingLoader();
            solution = solution.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), "D.cs", loader: loader));
        }

        return solution;
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void Check(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-check failed: " + failure);
        }
    }

    private sealed class BlockingLoader : TextLoader
    {
        public override async Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private sealed class FailingLoader(string message) : TextLoader
    {
        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }
}
