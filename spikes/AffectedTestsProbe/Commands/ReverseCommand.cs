using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Reverse search. This command does not build the forward graph.
/// <c>reverse</c> is one fresh process. <c>reverse-batch</c> keeps one process so later queries reuse compilations.
/// </summary>
internal static class ReverseCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var member = CommandArgs.Value(args, "--member") ?? throw new InvalidOperationException("Pass --member.");
        var started = Stopwatch.GetTimestamp();
        await using var session = await OpenAsync(args, cancellationToken).ConfigureAwait(false);
        await SearchPrewarm.WarmAsync(session.Solution, cancellationToken).ConfigureAwait(false);
        var prepareMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var report = await SearchAsync(session, member, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(
            new { series = "first-query", prepareMs, search = report },
            new JsonSerializerOptions { WriteIndented = true }));
        return report.Found ? 0 : 1;
    }

    public static async Task<int> BatchAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var file = CommandArgs.Value(args, "--members") ?? throw new InvalidOperationException("Pass --members.");
        var members = (await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        var repeat = CommandArgs.Integer(args, "--repeat", 0);
        var started = Stopwatch.GetTimestamp();
        await using var session = await OpenAsync(args, cancellationToken).ConfigureAwait(false);
        await SearchPrewarm.WarmAsync(session.Solution, cancellationToken).ConfigureAwait(false);
        var prepareMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var reports = new List<object>();
        var first = true;
        foreach (var member in members)
        {
            var report = await SearchAsync(session, member, cancellationToken).ConfigureAwait(false);
            reports.Add(Row(first ? "sequential-first" : "subsequent", member, report));
            first = false;
        }

        for (var pass = 0; pass < repeat; pass++)
        {
            foreach (var member in members)
            {
                var report = await SearchAsync(session, member, cancellationToken).ConfigureAwait(false);
                reports.Add(Row("warmed", member, report, pass));
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new { prepareMs, reports },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static object Row(string series, string member, ReverseReport report, int? pass = null) => new
    {
        series,
        pass,
        member,
        report.ElapsedMs,
        report.Visited,
        report.Limit,
        report.Answer,
    };

    private static async Task<Session> OpenAsync(string[] args, CancellationToken cancellationToken)
    {
        var path = CommandArgs.Value(args, "--solution") ?? throw new InvalidOperationException("Pass --solution.");
        var configuration = CommandArgs.Value(args, "--configuration") ?? "Debug";
        var platform = CommandArgs.Value(args, "--platform") ?? "AnyCPU";
        var workspace = await SolutionLoader.OpenAsync(path, configuration, platform, cancellationToken).ConfigureAwait(false);
        var solution = workspace.CurrentSolution;
        var map = new ProjectMap(solution, configuration, platform);
        var roots = await RootIndexBuilder.BuildAsync(solution, map, CommandArgs.Values(args, "--test-project"), cancellationToken)
            .ConfigureAwait(false);
        return new Session(workspace, solution, map, roots, CommandArgs.Value(args, "--project"));
    }

    private static async Task<ReverseReport> SearchAsync(Session session, string documentationId, CancellationToken cancellationToken)
    {
        var node = await FindAsync(session, documentationId, cancellationToken).ConfigureAwait(false);
        if (node is not { } start)
        {
            return ReverseReport.Missing(documentationId);
        }

        var outcome = await ReverseSearch.SearchAsync(session.Solution, session.Map, session.Roots, start, ProbeLimits.Default, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Limit is null)
        {
            var units = outcome.Units.Select(unit => unit.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            return ReverseReport.Finished(documentationId, "reverse", outcome, units);
        }

        var symbol = await ResolveAsync(session, start, cancellationToken).ConfigureAwait(false);
        var level0 = symbol is null
            ? []
            : LevelZero.Split(session.Solution, session.Map, session.Roots, symbol).InScope.Select(unit => unit.Key).ToArray();
        return ReverseReport.Finished(documentationId, "level0", outcome, level0);
    }

    private static async Task<MemberNode?> FindAsync(Session session, string documentationId, CancellationToken cancellationToken)
    {
        foreach (var project in session.Solution.Projects)
        {
            if (session.ProjectPath is not null
                && !string.Equals(project.FilePath, Path.GetFullPath(session.ProjectPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var symbol = compilation is null
                ? null
                : DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, compilation);
            if (symbol is not null && SymbolIds.TryNode(symbol, session.Solution, session.Map.Keys) is { } node)
            {
                return node;
            }
        }

        return null;
    }

    private static async Task<ISymbol?> ResolveAsync(Session session, MemberNode node, CancellationToken cancellationToken)
    {
        if (!session.Map.Projects.TryGetValue(node.Project, out var project))
        {
            return null;
        }

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        return compilation is null
            ? null
            : DocumentationCommentId.GetFirstSymbolForDeclarationId(node.DocumentationId, compilation);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace _workspace;

        public Session(
            Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace,
            Solution solution,
            ProjectMap map,
            RootIndex roots,
            string? projectPath)
        {
            _workspace = workspace;
            Solution = solution;
            Map = map;
            Roots = roots;
            ProjectPath = projectPath;
        }

        public Solution Solution { get; }

        public ProjectMap Map { get; }

        public RootIndex Roots { get; }

        public string? ProjectPath { get; }

        public async ValueTask DisposeAsync() => _workspace.Dispose();
    }

    private sealed record ReverseReport(
        bool Found,
        string DocumentationId,
        string Answer,
        long ElapsedMs,
        int Visited,
        int Depth,
        string? Limit,
        IReadOnlyList<string> Units)
    {
        public static ReverseReport Missing(string documentationId) =>
            new(false, documentationId, "missing", 0, 0, 0, null, []);

        public static ReverseReport Finished(string documentationId, string answer, ReverseOutcome outcome, IReadOnlyList<string> units) =>
            new(true, documentationId, answer, outcome.ElapsedMilliseconds, outcome.Visited, outcome.Depth, outcome.Limit, units);
    }
}
