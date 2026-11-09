using System.Diagnostics;
using System.Text.Json;

namespace AffectedTestsProbe;

/// <summary>
/// Writes frame B before the call graph is built, then frame A from that graph.
/// </summary>
internal static class SampleCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var path = CommandArgs.Value(args, "--solution") ?? throw new InvalidOperationException("Pass --solution.");
        var configuration = CommandArgs.Value(args, "--configuration") ?? "Debug";
        var platform = CommandArgs.Value(args, "--platform") ?? "AnyCPU";
        var tests = CommandArgs.Values(args, "--test-project");
        var seed = CommandArgs.Integer(args, "--seed", 1);
        using var workspace = await SolutionLoader.OpenAsync(path, configuration, platform, cancellationToken).ConfigureAwait(false);
        var solution = workspace.CurrentSolution;
        var map = new ProjectMap(solution, configuration, platform);
        var frameB = await SamplePlanner.FrameBAsync(solution, map, tests, seed, cancellationToken).ConfigureAwait(false);
        var hash = frameB.Hash;
        var started = Stopwatch.GetTimestamp();
        var facts = await FactExtractor.ExtractAsync(solution, map, cancellationToken).ConfigureAwait(false);
        var roots = await RootIndexBuilder.BuildAsync(solution, map, tests, cancellationToken).ConfigureAwait(false);
        var reach = ForwardReachability.Compute(facts, roots, map);
        var frameA = SelectFrameA(facts, reach, seed);
        var commit = await GitRevision.TryHeadAsync(Path.GetDirectoryName(Path.GetFullPath(path))!, cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(new
        {
            solution = Path.GetFullPath(path),
            commit,
            seed,
            frameBHash = hash,
            diEmpty = frameB.DiEmpty,
            frameB = frameB.Selected.Select(Item),
            frameBCounts = new
            {
                ordinary = frameB.Ordinary.Count,
                @virtual = frameB.Virtual.Count,
                di = frameB.Di.Count,
                @static = frameB.Static.Count,
            },
            graphMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            frameA,
        }, new JsonSerializerOptions { WriteIndented = true });
        var output = CommandArgs.Value(args, "--out");
        if (output is null)
        {
            Console.WriteLine(json);
        }
        else
        {
            await File.WriteAllTextAsync(output, json, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static object Item(SampleMember member) => new
    {
        member.Stratum,
        member.DocumentationId,
        project = member.Project.ProjectPath,
        tfm = member.Project.TargetFramework,
        member.Display,
        member.Reason,
    };

    private static object SelectFrameA(FactGraph facts, Reachability reach, int seed)
    {
        var ranked = reach.Cha
            .Select(pair => new Rank(pair.Key, pair.Value.Count, reach.ShortestCha.TryGetValue(pair.Key, out var depth) ? depth : 0))
            .OrderByDescending(item => item.Tests)
            .ThenByDescending(item => item.Depth)
            .ThenBy(item => item.Node.DocumentationId, StringComparer.Ordinal)
            .ToArray();
        var loaded = ranked.Where(item => item.Tests > 5).Take(5).ToArray();
        var loadedIds = loaded.Select(item => item.Node).ToHashSet();
        var leaves = Shuffle(ranked.Where(item => item.Tests is >= 1 and <= 5 && !loadedIds.Contains(item.Node)).ToArray(), seed).Take(5);
        var reached = reach.Cha.Keys.ToHashSet();
        var unreachable = Shuffle(
            facts.Facts.Keys.Where(node => !reached.Contains(node)).Select(node => new Rank(node, 0, 0)).ToArray(),
            seed).Take(4);
        return new
        {
            loaded = loaded.Select(Describe),
            leaves = leaves.Select(Describe),
            unreachable = unreachable.Select(Describe),
        };
    }

    private static object Describe(Rank rank) => new
    {
        rank.Node.DocumentationId,
        project = rank.Node.Project.ProjectPath,
        tfm = rank.Node.Project.TargetFramework,
        tests = rank.Tests,
        depth = rank.Depth,
    };

    private static IEnumerable<Rank> Shuffle(Rank[] items, int seed)
    {
        var copy = items.OrderBy(item => item.Node.DocumentationId, StringComparer.Ordinal).ToArray();
        var random = new Random(seed);
        for (var index = copy.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private readonly record struct Rank(MemberNode Node, int Tests, int Depth);
}
