using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Mutates one member in a clone or worktree, runs the level-0 test projects that sit in scope,
/// and restores the file. The printed sets are the completeness record; this command is not a price measurement.
/// </summary>
internal static class OracleCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!CommandArgs.Has(args, "--clone-ack"))
        {
            Console.Error.WriteLine("Refusing to edit a tree without --clone-ack. Use a clone or worktree.");
            return 2;
        }

        var path = CommandArgs.Value(args, "--solution") ?? throw new InvalidOperationException("Pass --solution.");
        var memberId = CommandArgs.Value(args, "--member") ?? throw new InvalidOperationException("Pass --member.");
        var configuration = CommandArgs.Value(args, "--configuration") ?? "Debug";
        var platform = CommandArgs.Value(args, "--platform") ?? "AnyCPU";
        var tests = CommandArgs.Values(args, "--test-project");
        if (tests.Count == 0)
        {
            throw new InvalidOperationException("Pass at least one --test-project.");
        }

        string file;
        string mutated;
        string[] projects;
        IReadOnlyList<TestUnit> units;
        string[] fCha;
        string[] fRta;
        string[] rCha;
        string[] level0;
        string rChaAnswer;
        string? limit;
        string[] outsideScope;
        using (var workspace = await SolutionLoader.OpenAsync(path, configuration, platform, cancellationToken).ConfigureAwait(false))
        {
            var solution = workspace.CurrentSolution;
            var map = new ProjectMap(solution, configuration, platform);
            var roots = await RootIndexBuilder.BuildAsync(solution, map, tests, cancellationToken).ConfigureAwait(false);
            var located = await LocateAsync(solution, map, memberId, cancellationToken).ConfigureAwait(false);
            if (located is null)
            {
                Console.Error.WriteLine("Member was not found.");
                return 1;
            }

            var (symbol, node, mutation) = located.Value;
            if (!mutation.Applied || mutation.Root is null)
            {
                Console.Error.WriteLine(mutation.Reason ?? "not mutable");
                return 1;
            }

            var facts = await FactExtractor.ExtractAsync(solution, map, cancellationToken).ConfigureAwait(false);
            var reach = ForwardReachability.Compute(facts, roots, map);
            var reverse = await ReverseSearch.SearchAsync(solution, map, roots, node, ProbeLimits.Default, cancellationToken)
                .ConfigureAwait(false);
            var split = LevelZero.Split(solution, map, roots, symbol);
            fCha = Keys(reach.Units(DispatchKind.Cha, node));
            fRta = Keys(reach.Units(DispatchKind.Rta, node));
            level0 = Keys(split.InScope);
            limit = reverse.Limit;
            rChaAnswer = reverse.Limit is null ? "reverse" : "level0";
            rCha = reverse.Limit is null ? Keys(reverse.Units) : level0;
            outsideScope = split.OutsideScope.Select(key => key.ToString()).ToArray();
            file = symbol.DeclaringSyntaxReferences.First().SyntaxTree.FilePath;
            mutated = mutation.Root.ToFullString();
            var inScopePaths = split.InScope
                .Select(unit => unit.Project.ProjectPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            projects = solution.Projects
                .Select(project => project.FilePath)
                .Where(projectPath => !string.IsNullOrEmpty(projectPath))
                .Select(projectPath => Path.GetFullPath(projectPath!))
                .Where(projectPath => inScopePaths.Contains(projectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            units = roots.Units.Where(unit => inScopePaths.Contains(unit.Project.ProjectPath)).ToArray();
        }

        if (projects.Length == 0 || string.IsNullOrEmpty(file))
        {
            Console.Error.WriteLine("No in-scope level-0 test project was found.");
            return 1;
        }

        var directory = Path.Combine(Path.GetTempPath(), "affected-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        var backup = file + ".tia-backup";
        await File.WriteAllBytesAsync(backup, original, cancellationToken).ConfigureAwait(false);
        var restored = false;
        try
        {
            var control = await RunProjectsAsync(
                projects, units, configuration, directory, "control", TimeSpan.FromMinutes(30), cancellationToken).ConfigureAwait(false);
            Run? mutationRun = null;
            PairJudgment? judgment = null;
            if (InvalidReason(control, null, null) is null)
            {
                await FileBytes.WriteTextPreservingBomAsync(file, mutated, original, cancellationToken).ConfigureAwait(false);
                var timeout = TimeSpan.FromMilliseconds(Math.Max(
                    control.Elapsed.TotalMilliseconds * 3,
                    TimeSpan.FromMinutes(5).TotalMilliseconds));
                mutationRun = await RunProjectsAsync(
                    projects, units, configuration, directory, "mutation", timeout, cancellationToken).ConfigureAwait(false);
                judgment = PairValidity.Judge(control.Cases, mutationRun.Cases);
            }

            var reason = InvalidReason(control, mutationRun, judgment);
            var oracle = judgment is null ? [] : OracleSet.Affected(judgment.FailedUnits, control.Cases);
            var controlFailed = control.Cases
                .Where(row => row.Outcome == "Failed")
                .Select(row => row.UnitKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(unit => unit, StringComparer.Ordinal)
                .ToArray();
            var json = JsonSerializer.Serialize(new
            {
                member = memberId,
                file,
                fileSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
                projects,
                fCha,
                fRta,
                rCha,
                l0 = level0,
                rChaAnswer,
                limit,
                outsideScope,
                oracle,
                controlFailed,
                emptyOracle = reason is null && oracle.Length == 0,
                publishCompleteness = CompletenessGate.Publish(control.Executed, control.Unmapped),
                controlExecuted = control.Executed,
                unmappedExecuted = control.Unmapped,
                judgment = judgment?.Status.ToString() ?? reason,
                invalid = reason,
                controlTimedOut = control.TimedOut,
                mutationTimedOut = mutationRun?.TimedOut ?? false,
            }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 0;
        }
        finally
        {
            await File.WriteAllBytesAsync(file, original, CancellationToken.None).ConfigureAwait(false);
            var written = await File.ReadAllBytesAsync(file, CancellationToken.None).ConfigureAwait(false);
            restored = written.AsSpan().SequenceEqual(original);
            if (!restored)
            {
                Console.Error.WriteLine("Restore failed. Backup remains at " + backup);
            }
            else if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
    }

    private static string? InvalidReason(Run control, Run? mutation, PairJudgment? judgment)
    {
        if (control.TimedOut || mutation?.TimedOut == true)
        {
            return "timeout";
        }

        if (control.BuildFailed || mutation?.BuildFailed == true)
        {
            return "build failed";
        }

        if (control.MissingArtifacts || mutation?.MissingArtifacts == true)
        {
            return "missing results";
        }

        if (control.Executed == 0)
        {
            return "zero executed";
        }

        return judgment?.Status switch
        {
            PairStatus.MissingResults => "missing results",
            PairStatus.SkippedMismatch => "skipped mismatch",
            _ => null,
        };
    }

    private static async Task<(ISymbol Symbol, MemberNode Node, Mutation Mutation)?> LocateAsync(
        Solution solution,
        ProjectMap map,
        string documentationId,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var symbol = compilation is null
                ? null
                : DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, compilation);
            if (symbol is null || SymbolIds.TryNode(symbol, solution, map.Keys) is not { } node)
            {
                continue;
            }

            return (symbol, node, MemberMutator.Apply(symbol));
        }

        return null;
    }

    private static async Task<Run> RunProjectsAsync(
        string[] projects,
        IReadOnlyList<TestUnit> units,
        string configuration,
        string directory,
        string label,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var cases = new List<ExecutedCase>();
        var executed = 0;
        var unmapped = 0;
        var timedOut = false;
        var buildFailed = false;
        var missingArtifacts = false;
        var watch = Stopwatch.StartNew();
        foreach (var project in projects)
        {
            var diag = Path.Combine(directory, label + "-" + Path.GetFileName(project) + ".diag");
            var trx = Path.Combine(directory, label + "-" + Path.GetFileName(project) + ".trx");
            var remaining = timeout - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                timedOut = true;
                break;
            }

            var result = await DotNetTest.RunAsync(project, configuration, diag, trx, remaining, cancellationToken)
                .ConfigureAwait(false);
            if (result.TimedOut)
            {
                timedOut = true;
                break;
            }

            var haveResults = File.Exists(diag) && File.Exists(trx);
            if (!haveResults)
            {
                if (result.ExitCode != 0)
                {
                    buildFailed = true;
                }
                else
                {
                    missingArtifacts = true;
                }

                continue;
            }

            var discovered = DiscoveryLog.Read(await File.ReadAllTextAsync(diag, cancellationToken).ConfigureAwait(false));
            var rows = TrxResults.Read(await File.ReadAllTextAsync(trx, cancellationToken).ConfigureAwait(false));
            var joined = RunJoin.Join(discovered, rows, units);
            executed += joined.Executed;
            unmapped += joined.UnmappedExecuted;
            cases.AddRange(joined.Cases);
        }

        return new Run(cases, executed, unmapped, timedOut, buildFailed, missingArtifacts, watch.Elapsed);
    }

    private static string[] Keys(IEnumerable<TestUnit> units) =>
        units.Select(unit => unit.Key).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private sealed record Run(
        IReadOnlyList<ExecutedCase> Cases,
        int Executed,
        int Unmapped,
        bool TimedOut,
        bool BuildFailed,
        bool MissingArtifacts,
        TimeSpan Elapsed);
}
