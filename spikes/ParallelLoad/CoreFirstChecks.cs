using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstChecks
{
    public static int Run()
    {
        CheckPartition();
        CheckLoadInputs();
        CheckCluster();
        CheckSnapshotLoad();
        CheckHostObservation();
        CheckMerge();
        CheckFailureEvidenceAsync().GetAwaiter().GetResult();
        Console.WriteLine("Core-first checks passed: shared core, cluster balance and refusal, load inputs, snapshot ROA=false edges, sampled CPU, downward closure, directory promotion, cycles, IDs, TFM variants, aliases, aggregator removal, and failure/cancellation evidence.");
        return 0;
    }

    private static void CheckCluster()
    {
        var paths = new[] { "A/One.csproj", "B/Two.csproj", "C/Three.csproj", "D/Four.csproj", "E/SharedA.csproj", "F/SharedB.csproj" }
            .Select(Path.GetFullPath).ToArray();
        var graph = paths.ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        graph[paths[0]].Add(paths[4]);
        graph[paths[1]].Add(paths[4]);
        graph[paths[2]].Add(paths[5]);
        graph[paths[3]].Add(paths[5]);
        var costs = paths.ToDictionary(path => path, path => path == paths[4] || path == paths[5] ? 3d : 1d,
            StringComparer.OrdinalIgnoreCase);
        var greedy = CoreFirstPartition.Create(graph, costs, 2);
        var cluster = CoreFirstPartition.Create(graph, costs, 2, partition: "cluster");
        Require(cluster.BalanceSatisfied && cluster.CoreCost < greedy.CoreCost,
            "Balanced pair swaps must colocate related roots and reduce shared-core cost.");
        Require(cluster.DownwardClosed && cluster.SharedDirectoriesSafe &&
            cluster.Core.Length + cluster.Groups.Sum(group => group.Length) == paths.Length,
            "Cluster optimization must preserve complete coverage, core closure, and shared directory ownership.");
        var repeated = CoreFirstPartition.Create(graph, costs, 2, partition: "cluster");
        Require(JsonSerializer.Serialize(cluster) == JsonSerializer.Serialize(repeated),
            "The same graph and costs must produce a deterministic cluster plan.");
        var singlePath = Path.GetFullPath("Single/Root.csproj");
        var singleGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase) { [singlePath] = [] };
        var singleCosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [singlePath] = 10 };
        var infeasible = CoreFirstPartition.Create(singleGraph, singleCosts, 4, partition: "cluster");
        Require(!infeasible.BalanceSatisfied && infeasible.BalanceRatio == 4 &&
            infeasible.PlanningStatus == "infeasible-search" && infeasible.PlanningReason!.Contains("all 4 workers"),
            "An indivisible root must produce explicit refusal rather than exclude empty workers or relax epsilon.");
        var zero = CoreFirstPartition.Create(singleGraph, new Dictionary<string, double> { [singlePath] = 0 }, 4,
            partition: "cluster");
        Require(zero.BalanceSatisfied && zero.BalanceRatio == 1,
            "A zero-cost closed graph must have a defined zero-over-zero balance convention.");
    }

    private static void CheckSnapshotLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ParallelLoad-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "Root", "Root.csproj");
            var child = Path.Combine(directory, "Child", "Child.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            Directory.CreateDirectory(Path.GetDirectoryName(child)!);
            var solution = Path.Combine(directory, "Test.slnx");
            var snapshot = Path.Combine(directory, "baseline.json");
            File.WriteAllText(root, "<Project><ItemGroup><ProjectReference Include=\"../Child/Child.csproj\"><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference></ItemGroup></Project>");
            File.WriteAllText(child, "<Project />");
            File.WriteAllText(solution, "<Solution><Project Path=\"Root/Root.csproj\" /><Project Path=\"Child/Child.csproj\" /></Solution>");
            File.WriteAllText(snapshot, JsonSerializer.Serialize(new
            {
                Status = "ok",
                Projects = new Dictionary<string, object>
                {
                    [root + "|Root"] = new { ProjectReferences = "[]" },
                    [child + "|Child"] = new { ProjectReferences = "[]" },
                },
            }));
            var options = new RunOptions("core-first", solution, Path.Combine(directory, "result.json"), 2, "cluster")
            {
                Graph = "snapshot", SnapshotPath = snapshot,
            };
            var compilationOnly = CoreFirstPartition.Create(options);
            var loadGraph = CoreFirstPartition.Create(options with { Graph = "snapshot-load" });
            Require(compilationOnly.Roots.Sum(group => group.Length) == 2 && loadGraph.Roots.Sum(group => group.Length) == 1,
                "Snapshot-load must add literal ROA=false metadata edges absent from compilation references.");
            Require(loadGraph.Core.Concat(loadGraph.Groups.SelectMany(group => group)).Distinct().Count() == 2,
                "Adding load edges must preserve both projects in graph coverage.");
            RequireThrows(() => CoreFirstPartition.Create(options with { Graph = "snapshot-load" }, [root]),
                "Recursive core planning must reject a subset missing a dependency.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckHostObservation()
    {
        var observation = new BuildHostObservation(0, 0);
        observation.Observe(100, 200);
        observation.Observe(200, 200);
        observation.Observe(300, 600);
        var metrics = observation.Capture("synthetic", 600);
        Require(metrics.SampledLifetimeMs == 300 && metrics.SampledZeroCpuMs == 100 &&
            metrics.SampledZeroCpuShare == 1d / 3 && metrics.CpuMs == 600 && metrics.Observations == 4,
            "Zero-CPU interval sampling must remain valid when a multithreaded host uses more CPU than elapsed wall time.");
        Require(new BuildHostObservation(10, 2).Capture("short", 2).SampledZeroCpuShare is null,
            "A host observed only once has no measured zero-CPU fraction.");
    }

    private static void CheckLoadInputs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ParallelLoad-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var solution = Path.Combine(directory, "Test.slnx");
            File.WriteAllText(solution, "<Solution><Project Path=\"Core.csproj\" /><Project Path=\"Root.csproj\" /><Project Path=\"Native.vcxproj\" /><Project Path=\"Native.vcxproj\" /></Solution>");
            var core = Path.Combine(directory, "Core.csproj");
            var root = Path.Combine(directory, "Root.csproj");
            var native = Path.Combine(directory, "Native.vcxproj");
            var plan = new CoreFirstPlan("synthetic", "count", [core], [[root]], [[root]], 1, 2, [1], true, true);
            var requests = CoreFirstLoadInputs.Phase1Requests(solution, plan);
            Require(requests.SequenceEqual([core, native]),
                "Phase one must request the core and each unloadable original solution entry exactly once.");
            Require(CoreFirstLoadInputs.SolutionDirectory(solution) == directory + Path.DirectorySeparatorChar,
                "SolutionDir must use the original solution directory with its trailing separator.");
            var filter = ProjectPartition.WriteFilter(solution, requests, directory, -1);
            using var filterJson = JsonDocument.Parse(File.ReadAllText(filter));
            Require(filterJson.RootElement.GetProperty("solution").GetProperty("path").GetString() == solution,
                "The temporary filter must point at the original solution rather than its own directory.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckPartition()
    {
        var paths = new[] { "A/A.csproj", "B/B.csproj", "Shared/C.csproj", "Shared/D.csproj", "Leaf/L.csproj", "Cycle/X.csproj", "Cycle/Y.csproj" }
            .Select(Path.GetFullPath).ToArray();
        var graph = paths.ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        graph[paths[0]].Add(paths[2]);
        graph[paths[1]].Add(paths[3]);
        graph[paths[2]].Add(paths[4]);
        graph[paths[3]].Add(paths[4]);
        graph[paths[5]].Add(paths[6]);
        graph[paths[6]].Add(paths[5]);
        var costs = paths.ToDictionary(path => path, _ => 1d, StringComparer.OrdinalIgnoreCase);
        var plan = CoreFirstPartition.Create(graph, costs, 2);
        Require(plan.Core.Contains(paths[2]) && plan.Core.Contains(paths[3]) && plan.Core.Contains(paths[4]),
            "Cross-group shared directories and their dependencies must be promoted into the core.");
        Require(plan.DownwardClosed && plan.SharedDirectoriesSafe, "The core must be closed and directory ownership safe.");
        Require(plan.Core.Concat(plan.Groups.SelectMany(group => group)).Distinct().Count() == paths.Length,
            "Rootless cycles and every project must be covered.");
        Require(plan.Groups.SelectMany(group => group).Count() + plan.Core.Length == paths.Length,
            "No non-core project may repeat across groups.");
        var single = CoreFirstPartition.Create(graph, costs, 1);
        Require(single.Core.Length == 0, "One worker must have no shared core.");
        Require(single.Groups[0].Length == paths.Length, "One worker must cover the complete graph.");
        graph[paths[0]].Add("missing.csproj");
        RequireThrows(() => CoreFirstPartition.Create(graph, costs, 2), "An open graph must be rejected.");
    }

    private static void CheckMerge()
    {
        var corePath = Path.GetFullPath("Core/Core.csproj");
        var rootPath = Path.GetFullPath("Root/Root.csproj");
        var aggregatorPath = Path.GetFullPath("Aggregator/Group.csproj");
        var core = Create(corePath, "Core(net10.0)");
        var otherFramework = Create(corePath, "Core(net9.0)");
        var aggregator = Create(aggregatorPath, "Group");
        var aliases = ImmutableArray.Create("coreAlias");
        var nativeId = ProjectId.CreateNewId();
        var root = Create(rootPath, "Root").WithProjectReferences([
            new ProjectReference(core.Id, aliases, embedInteropTypes: true), new ProjectReference(otherFramework.Id),
            new ProjectReference(aggregator.Id), new ProjectReference(nativeId),
        ]);
        var progress = new PhaseLoadProgress(TimeProvider.System);
        var groups = new[] { new CoreFirstGroup(0, aggregatorPath, [root, aggregator, core], progress) };
        Require(CoreFirstMerger.FindViolations([core, otherFramework], groups).Length == 0,
            "Borrowed core ProjectInfo with the original ID is not a rebuilt project.");
        var merged = CoreFirstMerger.Merge([core, otherFramework], groups, "test.slnx");
        Require(merged.Projects.Count == 3 && merged.Projects.All(project => project.Id != aggregator.Id),
            "Aggregator projects must be removed; core TFM variants must remain.");
        var references = merged.Projects.Single(project => project.Id == root.Id).ProjectReferences;
        Require(references.Count == 3 && references[0].ProjectId == core.Id && references[1].ProjectId == otherFramework.Id,
            "Core IDs must survive without reference rewriting.");
        Require(references[0].Aliases.SequenceEqual(aliases) && references[0].EmbedInteropTypes && references[2].ProjectId == nativeId,
            "Aliases, interop embedding, and opaque native targets must survive merging.");
        var duplicate = new CoreFirstGroup(1, null, [Create(rootPath, "Root")], new PhaseLoadProgress(TimeProvider.System));
        Require(CoreFirstMerger.FindViolations([core], [groups[0], duplicate]).Length > 0,
            "Non-core path duplication across workers must fail the plan.");
        var rebuilt = new CoreFirstGroup(0, null, [Create(corePath, "Core(net10.0)")], progress);
        Require(CoreFirstMerger.FindViolations([core], [rebuilt]).Length > 0,
            "A core project with a different ID must fail the plan.");
        progress.Start();
        progress.ObservePath(corePath);
        Require(CoreFirstMerger.FindViolations([core], groups).Length > 0,
            "Core build progress in a worker must fail even if its returned ID was reused.");
    }

    private static ProjectInfo Create(string path, string name) => ProjectInfo.Create(
        ProjectId.CreateNewId(name), VersionStamp.Create(), name, name, LanguageNames.CSharp, filePath: path);

    private static async Task CheckFailureEvidenceAsync()
    {
        var metrics = new ProcessMetrics(1, 1, 1024, 10, 10, 1, 100, 1, []);
        var stopCalls = 0;
        var finalization = new SamplerFinalization((_, _) =>
        {
            stopCalls++;
            return Task.FromResult(metrics);
        });
        var captured = await finalization.StopAsync(100, 10);
        string? observationFailure = null;
        try
        {
            throw new InvalidDataException("Injected observation failure after sampler stop.");
        }
        catch (InvalidDataException ex)
        {
            observationFailure = ex.Message;
            var evidence = await FailureEvidenceCapture.CaptureAsync(ex, finalization, 200, 20);
            using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(evidence));
            Require(serialized.RootElement.GetProperty("Error").GetString()!.Contains(observationFailure) &&
                serialized.RootElement.GetProperty("Process").GetProperty("BuildHostCount").GetInt32() == 1 &&
                serialized.RootElement.GetProperty("SamplerFinalizationError").ValueKind == JsonValueKind.Null,
                "The serialized failure evidence must preserve the observation cause and original sampled metrics.");
            Require(ReferenceEquals(captured, evidence.Process),
                "Failure finalization must reuse the first metrics instead of stopping the sampler again.");
        }

        Require(stopCalls == 1 && observationFailure == "Injected observation failure after sampler stop.",
            "An observation exception must retain its original evidence and stop the sampler exactly once.");
        var failedStopCalls = 0;
        var failedFinalization = new SamplerFinalization((_, _) =>
        {
            failedStopCalls++;
            throw new IOException("Injected sampler failure.");
        });
        var fallback = await failedFinalization.StopAsync(100, 10);
        Require(ReferenceEquals(fallback, await failedFinalization.StopAsync(200, 20)) && failedStopCalls == 1,
            "Sampler failure must produce cached fallback metrics without a second stop attempt.");
        Require(fallback.Errors.Single().Contains("Injected sampler failure.") &&
            failedFinalization.Error!.Contains("Injected sampler failure."), "Sampler failure must retain its original cause.");
        var originalFailure = new IOException("Injected original load failure.");
        var doubleFailure = await FailureEvidenceCapture.CaptureAsync(originalFailure, failedFinalization, 200, 20);
        using var failureJson = JsonDocument.Parse(JsonSerializer.Serialize(doubleFailure));
        Require(failureJson.RootElement.GetProperty("Error").GetString()!.Contains("Injected original load failure.") &&
            failureJson.RootElement.GetProperty("SamplerFinalizationError").GetString()!.Contains("Injected sampler failure."),
            "A serialized failure row must retain the original load error separately from sampler finalization failure.");
        await CheckGroupFailuresAsync();
    }

    private static async Task CheckGroupFailuresAsync()
    {
        Require(FailureEvidenceCapture.CoreFirstStatus(false, true, false) == "plan-violation" &&
            FailureEvidenceCapture.CoreFirstStatus(true, true, false) == "plan-violation" &&
            FailureEvidenceCapture.CoreFirstStatus(false, false, true) == "infeasible-search" &&
            FailureEvidenceCapture.CoreFirstStatus(true, false, false) == "timeout",
            "Top-level and recursive ownership violations must retain their explicit status alongside failure evidence.");
        var loaded = Create(Path.GetFullPath("Partial/Partial.csproj"), "Partial");
        var success = new CoreFirstGroup(0, null, [], new PhaseLoadProgress(TimeProvider.System));
        var failure = new CoreFirstGroup(1, null, [], new PhaseLoadProgress(TimeProvider.System));
        var timeout = new CoreFirstGroup(2, null, [], new PhaseLoadProgress(TimeProvider.System));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Task.WhenAll(
            CoreFirstGroupExecution.RunAsync(success, async () =>
            {
                await Task.Yield();
                success.Projects = [loaded];
                success.Progress.ObservePath(loaded.FilePath!);
            }, CancellationToken.None),
            CoreFirstGroupExecution.RunAsync(failure, async () =>
            {
                failure.Projects = [loaded];
                failure.Progress.ObservePath(loaded.FilePath!);
                await Task.Yield();
                throw new IOException("Injected group failure after a partial load.");
            }, CancellationToken.None),
            CoreFirstGroupExecution.RunAsync(timeout, () =>
            {
                timeout.Progress.ObservePath(loaded.FilePath!);
                return Task.FromCanceled(canceled.Token);
            }, canceled.Token));
        Require(success.Status == "ok" && success.Projects.Single().Id == loaded.Id,
            "A neighboring failed group must not discard a successful group's projects.");
        Require(failure.Status == "failed" && failure.Projects.Single().Id == loaded.Id &&
            failure.Error!.Contains("Injected group failure after a partial load."),
            "A failed group must preserve partial projects and the original failure.");
        Require(timeout.Status == "timeout" && timeout.Error is not null,
            "A canceled group must retain an explicit timeout outcome.");
        Require(new[] { success, failure, timeout }.All(group => group.Progress.LoadedPaths.Length == 1 &&
            group.Progress.FirstProgressMs is not null && group.Progress.ElapsedMs >= 0),
            "Successful, failed, and timed-out groups must retain their phase timing and observed paths.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
