using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Mutates one member in a clone or worktree, runs each in-scope level-0 test target framework
/// separately, and restores the file. The printed sets are the completeness record.
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

        var prediction = await PredictAsync(path, memberId, configuration, platform, tests, cancellationToken).ConfigureAwait(false);
        if (prediction is null)
        {
            return 1;
        }

        await ExecuteAsync(prediction, configuration, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<Prediction?> PredictAsync(
        string path,
        string memberId,
        string configuration,
        string platform,
        IReadOnlyList<string> tests,
        CancellationToken cancellationToken)
    {
        using var workspace = await SolutionLoader.OpenAsync(path, configuration, platform, cancellationToken).ConfigureAwait(false);
        var solution = workspace.CurrentSolution;
        var map = new ProjectMap(solution, configuration, platform);
        var roots = await RootIndexBuilder.BuildAsync(solution, map, tests, cancellationToken).ConfigureAwait(false);
        var locations = await MemberLocations.FindAsync(solution, map, memberId, projectPath: null, cancellationToken)
            .ConfigureAwait(false);
        if (locations.Count == 0)
        {
            Console.Error.WriteLine("Member was not found.");
            return null;
        }

        var mutation = locations.Select(location => MemberMutator.Apply(location.Symbol)).FirstOrDefault(item => item.Applied);
        if (mutation is not { Applied: true, Root: not null })
        {
            Console.Error.WriteLine(mutation?.Reason ?? "not mutable");
            return null;
        }

        var symbol = locations[0].Symbol;
        var facts = await FactExtractor.ExtractAsync(solution, map, cancellationToken).ConfigureAwait(false);
        var reach = ForwardReachability.Compute(facts, roots, map);
        var split = LevelZero.Split(solution, map, roots, symbol);
        var reverse = await ReverseAllAsync(solution, map, roots, locations, cancellationToken).ConfigureAwait(false);
        var level0 = Keys(split.InScope);
        var targets = TestTarget.Group(split.InScope);
        if (targets.Count == 0 || string.IsNullOrEmpty(symbol.DeclaringSyntaxReferences.First().SyntaxTree.FilePath))
        {
            Console.Error.WriteLine("No in-scope level-0 test project was found.");
            return null;
        }

        return new Prediction(
            memberId,
            symbol.DeclaringSyntaxReferences.First().SyntaxTree.FilePath,
            mutation.Root.ToFullString(),
            targets,
            MemberLocations.ForwardKeys(reach, DispatchKind.Cha, locations),
            MemberLocations.ForwardKeys(reach, DispatchKind.Rta, locations),
            reverse.Limit is null ? reverse.Units : level0,
            level0,
            reverse.Limit is null ? "reverse" : "level0",
            reverse.Limit,
            split.OutsideScope.Select(key => key.ToString()).ToArray());
    }

    private static async Task<ReverseBundle> ReverseAllAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        IReadOnlyList<MemberLocation> locations,
        CancellationToken cancellationToken)
    {
        var units = new HashSet<string>(StringComparer.Ordinal);
        string? limit = null;
        foreach (var location in locations)
        {
            var outcome = await ReverseSearch.SearchAsync(
                solution, map, roots, location.Node, ProbeLimits.Default, cancellationToken).ConfigureAwait(false);
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

        var ordered = units.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        return new ReverseBundle(limit, ordered);
    }

    private static async Task ExecuteAsync(Prediction prediction, string configuration, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "affected-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = await File.ReadAllBytesAsync(prediction.File, cancellationToken).ConfigureAwait(false);
        var backup = prediction.File + ".tia-backup";
        await File.WriteAllBytesAsync(backup, original, cancellationToken).ConfigureAwait(false);
        try
        {
            var control = await RunTargetsAsync(prediction.Targets, configuration, directory, "control", null, cancellationToken)
                .ConfigureAwait(false);
            TargetBatch? mutation = null;
            var invalid = PairOutcome.Invalid(control.Signals, null);
            if (invalid is null)
            {
                await FileBytes.WriteTextPreservingBomAsync(prediction.File, prediction.Mutated, original, cancellationToken)
                    .ConfigureAwait(false);
                mutation = await RunTargetsAsync(
                    prediction.Targets, configuration, directory, "mutation", control.Elapsed, cancellationToken).ConfigureAwait(false);
                invalid = PairOutcome.Invalid(control.Signals, mutation.Signals);
            }

            PairJudgment? judgment = null;
            if (invalid is null && mutation is not null)
            {
                judgment = PairValidity.Judge(control.Cases, mutation.Cases);
                invalid = PairOutcome.AfterJudgment(judgment);
            }

            WriteReport(prediction, original, control, mutation, judgment, invalid);
        }
        finally
        {
            await RestoreAsync(prediction.File, original, backup).ConfigureAwait(false);
        }
    }

    private static void WriteReport(
        Prediction prediction,
        byte[] original,
        TargetBatch control,
        TargetBatch? mutation,
        PairJudgment? judgment,
        string? invalid)
    {
        var oracle = judgment is null ? [] : OracleSet.Affected(judgment.FailedUnits, control.Cases);
        var controlFailed = control.Cases
            .Where(row => row.Outcome == "Failed")
            .Select(row => row.UnitKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(unit => unit, StringComparer.Ordinal)
            .ToArray();
        var json = JsonSerializer.Serialize(new
        {
            member = prediction.MemberId,
            file = prediction.File,
            fileSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
            targets = prediction.Targets.Select(target => new { target.ProjectPath, target.TargetFramework }),
            fCha = prediction.FCha,
            fRta = prediction.FRta,
            rCha = prediction.RCha,
            l0 = prediction.Level0,
            rChaAnswer = prediction.RChaAnswer,
            limit = prediction.Limit,
            outsideScope = prediction.OutsideScope,
            oracle,
            controlFailed,
            emptyOracle = invalid is null && oracle.Length == 0,
            publishCompleteness = CompletenessGate.Publish(control.Signals.Executed, control.Unmapped),
            controlExecuted = control.Signals.Executed,
            unmappedExecuted = control.Unmapped,
            judgment = PairOutcome.JudgmentText(invalid, judgment),
            invalid,
            controlTimedOut = control.Signals.TimedOut,
            mutationTimedOut = mutation?.Signals.TimedOut ?? false,
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private static async Task<TargetBatch> RunTargetsAsync(
        IReadOnlyList<TestTarget> targets,
        string configuration,
        string directory,
        string label,
        IReadOnlyList<TimeSpan>? controlElapsed,
        CancellationToken cancellationToken)
    {
        var cases = new List<ExecutedCase>();
        var runs = new List<TargetRun>();
        var unmapped = 0;
        for (var index = 0; index < targets.Count; index++)
        {
            var timeout = controlElapsed is null
                ? TimeSpan.FromMinutes(30)
                // Three times this target's own control run, and at least five minutes.
                // One budget for the whole sample would starve a later target framework.
                : TimeSpan.FromTicks(Math.Max(controlElapsed[index].Ticks * 3, TimeSpan.FromMinutes(5).Ticks));
            var run = await RunTargetAsync(targets[index], configuration, directory, label, timeout, cancellationToken)
                .ConfigureAwait(false);
            runs.Add(run);
            unmapped += run.Unmapped;
            cases.AddRange(run.Cases);
        }

        return new TargetBatch(Merge(runs), cases, unmapped, runs.Select(run => run.Elapsed).ToArray());
    }

    private static async Task<TargetRun> RunTargetAsync(
        TestTarget target,
        string configuration,
        string directory,
        string label,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var suffix = label + "-" + Path.GetFileName(target.ProjectPath) + "-" + target.TargetFramework.Replace(':', '_');
        var diag = Path.Combine(directory, suffix + ".diag");
        var trx = Path.Combine(directory, suffix + ".trx");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await DotNetTest.RunAsync(
            target.ProjectPath, configuration, target.TargetFramework, diag, trx, timeout, cancellationToken).ConfigureAwait(false);
        var elapsed = started.Elapsed;
        if (result.TimedOut)
        {
            return new TargetRun(new RunSignals(true, false, false, false, 0, false), [], 0, elapsed);
        }

        if (!File.Exists(diag) || !File.Exists(trx))
        {
            return new TargetRun(MissingSignals(result.ExitCode, result.Output), [], 0, elapsed);
        }

        var discovered = DiscoveryLog.Read(await File.ReadAllTextAsync(diag, cancellationToken).ConfigureAwait(false));
        var rows = TrxResults.Read(await File.ReadAllTextAsync(trx, cancellationToken).ConfigureAwait(false));
        var joined = RunJoin.Join(discovered, rows, target.Units);
        var signals = new RunSignals(false, false, false, false, joined.Executed, joined.Executed == 0);
        return new TargetRun(signals, joined.Cases, joined.UnmappedExecuted, elapsed);
    }

    // `Build FAILED` is also printed after failed tests, so it is not a build signal.
    // A compiler error with no results is a failed build. Any other non-zero exit with no
    // results is a testhost crash, not a missing log.
    private static RunSignals MissingSignals(int exitCode, string output)
    {
        var buildFailed = output.Contains("error CS", StringComparison.Ordinal)
            || output.Contains("error MSB", StringComparison.Ordinal)
            || output.Contains(": error ", StringComparison.Ordinal);
        var crashed = !buildFailed && exitCode != 0;
        return new RunSignals(false, buildFailed, crashed, !buildFailed && !crashed, 0, false);
    }

    private static RunSignals Merge(IReadOnlyList<TargetRun> runs) => new(
        runs.Any(run => run.Signals.TimedOut),
        runs.Any(run => run.Signals.BuildFailed),
        runs.Any(run => run.Signals.Crashed),
        runs.Any(run => run.Signals.MissingArtifacts),
        runs.Sum(run => run.Signals.Executed),
        runs.Any(run => run.Signals.ZeroExecuted));

    private static string[] Keys(IEnumerable<TestUnit> units) =>
        units.Select(unit => unit.Key).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private static async Task RestoreAsync(string file, byte[] original, string backup)
    {
        await File.WriteAllBytesAsync(file, original, CancellationToken.None).ConfigureAwait(false);
        var written = await File.ReadAllBytesAsync(file, CancellationToken.None).ConfigureAwait(false);
        if (!written.AsSpan().SequenceEqual(original))
        {
            Console.Error.WriteLine("Restore failed. Backup remains at " + backup);
            return;
        }

        if (File.Exists(backup))
        {
            File.Delete(backup);
        }
    }

    private sealed record Prediction(
        string MemberId,
        string File,
        string Mutated,
        IReadOnlyList<TestTarget> Targets,
        string[] FCha,
        string[] FRta,
        string[] RCha,
        string[] Level0,
        string RChaAnswer,
        string? Limit,
        string[] OutsideScope);

    private sealed record TargetRun(RunSignals Signals, IReadOnlyList<ExecutedCase> Cases, int Unmapped, TimeSpan Elapsed);

    private sealed record TargetBatch(RunSignals Signals, IReadOnlyList<ExecutedCase> Cases, int Unmapped, IReadOnlyList<TimeSpan> Elapsed);

    private sealed record ReverseBundle(string? Limit, string[] Units);
}

