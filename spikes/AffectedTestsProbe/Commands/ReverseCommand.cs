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
        var locations = await MemberLocations.FindAsync(
            session.Solution, session.Map, documentationId, session.ProjectPath, cancellationToken).ConfigureAwait(false);
        if (locations.Count == 0)
        {
            return ReverseReport.Missing(documentationId);
        }

        var units = new HashSet<string>(StringComparer.Ordinal);
        long elapsed = 0;
        var visited = 0;
        var depth = 0;
        string? limit = null;
        foreach (var location in locations)
        {
            var outcome = await ReverseSearch.SearchAsync(
                session.Solution, session.Map, session.Roots, location.Node, ProbeLimits.Default, cancellationToken).ConfigureAwait(false);
            elapsed += outcome.ElapsedMilliseconds;
            visited += outcome.Visited;
            depth = Math.Max(depth, outcome.Depth);
            if (outcome.Limit is not null)
            {
                limit = outcome.Limit;
                break;
            }

            foreach (var unit in outcome.Units)
            {
                units.Add(unit.Key);
            }
        }

        if (limit is null)
        {
            var ordered = units.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            return ReverseReport.Aggregate(documentationId, "reverse", elapsed, visited, depth, null, ordered);
        }

        var level0 = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            foreach (var unit in LevelZero.Split(session.Solution, session.Map, session.Roots, location.Symbol).InScope)
            {
                level0.Add(unit.Key);
            }
        }

        return ReverseReport.Aggregate(
            documentationId,
            "level0",
            elapsed,
            visited,
            depth,
            limit,
            level0.OrderBy(key => key, StringComparer.Ordinal).ToArray());
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

        public static ReverseReport Aggregate(
            string documentationId,
            string answer,
            long elapsedMs,
            int visited,
            int depth,
            string? limit,
            IReadOnlyList<string> units) =>
            new(true, documentationId, answer, elapsedMs, visited, depth, limit, units);
    }
}
