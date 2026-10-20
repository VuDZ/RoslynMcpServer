using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using ParallelLoad.Models;

namespace ParallelLoad;

[SupportedOSPlatform("windows")]
internal static class CoreFirstExperiment
{
    public static async Task<int> RunAsync(RunOptions options, string registration, TimeProvider? timeProvider = null,
        CoreFirstPlan? suppliedPlan = null)
    {
        timeProvider ??= TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        var temporary = Path.Combine(Path.GetTempPath(), "ParallelLoad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var diagnostics = new ConcurrentQueue<string>();
        var workspaces = new List<AdhocWorkspace>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(options.TimeoutMinutes));
        using var process = Process.GetCurrentProcess();
        ExternalSampler? sampler = null;
        var finalization = new SamplerFinalization((wall, cpu) => sampler is not null
            ? sampler.StopAsync(wall, cpu)
            : Task.FromException<ProcessMetrics>(new InvalidOperationException("The external sampler did not start.")));
        var initialCpuMs = process.TotalProcessorTime.TotalMilliseconds;
        var start = timeProvider.GetTimestamp();
        var phase1 = new PhaseLoadProgress(timeProvider);
        CoreFirstPlan? plan = null;
        CoreFirstGroup[] groups = [];
        ProjectInfo[] core = [];
        string[] violations = [];
        var observation = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        double planningMs = 0;
        double preparationMs = 0;
        double mergeMs = 0;
        int unresolved = 0;
        string? error = null;
        var status = "ok";
        var restoreLogs = new List<string>();
        string[] phase1Requests = [];
        string? phase1SolutionDirectory = null;
        var expectedSolutionDirectory = CoreFirstLoadInputs.SolutionDirectory(options.SolutionPath);
        RecursiveCoreExecution? recursive = null;
        try
        {
            sampler = await ExternalSampler.StartAsync(temporary);
            initialCpuMs = process.TotalProcessorTime.TotalMilliseconds;
            start = timeProvider.GetTimestamp();
            phase1 = new PhaseLoadProgress(timeProvider, origin: start);
            var planningStart = timeProvider.GetTimestamp();
            try
            {
                plan = suppliedPlan ?? CoreFirstPartition.Create(options);
                phase1Requests = CoreFirstLoadInputs.Phase1Requests(options.SolutionPath, plan);
                if (options.Partition == "cluster" && !plan.BalanceSatisfied)
                {
                    throw new InvalidDataException(plan.PlanningReason);
                }

                if (options.CoreDepth == 2 && plan.Core.Length > 0)
                {
                    var nestedPlan = CoreFirstPartition.Create(options, plan.Core);
                    recursive = new RecursiveCoreExecution(nestedPlan, new PhaseLoadProgress(timeProvider, phase1, start));
                    if (options.Partition == "cluster" && !nestedPlan.BalanceSatisfied)
                    {
                        throw new InvalidDataException("Recursive core: " + nestedPlan.PlanningReason);
                    }
                }
            }
            finally
            {
                // A planning refusal is still measured work, including a rejected recursive plan.
                planningMs = timeProvider.GetElapsedTime(planningStart).TotalMilliseconds;
            }
            var preparationStart = timeProvider.GetTimestamp();
            var aggregatorPaths = new string?[options.Workers];
            if (options.Aggregator != "sequential")
            {
                for (var index = 0; index < plan.Roots.Length; index++)
                {
                    if (plan.Roots[index].Length == 0)
                    {
                        continue;
                    }

                    aggregatorPaths[index] = CoreFirstAggregator.Write(options.SolutionPath, temporary, index, plan.Roots[index]);
                    if (options.Aggregator == "restore")
                    {
                        restoreLogs.Add(await CoreFirstAggregator.RestoreAsync(aggregatorPaths[index]!, registration, timeout.Token));
                    }
                }
            }

            preparationMs = timeProvider.GetElapsedTime(preparationStart).TotalMilliseconds;
            var coreWorkspace = CreateWorkspace(diagnostics);
            workspaces.Add(coreWorkspace);
            Solution coreSolution;
            if (recursive is not null)
            {
                phase1.Start();
                try
                {
                    var recursiveInfo = await LoadRecursiveCoreAsync(
                        options, recursive, phase1Requests.Except(plan.Core, StringComparer.OrdinalIgnoreCase).ToArray(),
                        Path.Combine(temporary, "recursive"), diagnostics, workspaces, phase1,
                        timeProvider, start, registration, timeout.Token);
                    core = recursiveInfo.Projects.ToArray();
                    coreSolution = coreWorkspace.AddSolution(recursiveInfo);
                    phase1SolutionDirectory = recursive.SolutionDirectory;
                }
                finally
                {
                    phase1.Stop();
                }
            }
            else if (phase1Requests.Length == 0)
            {
                coreSolution = coreWorkspace.CurrentSolution;
            }
            else
            {
                var filter = ProjectPartition.WriteFilter(options.SolutionPath, phase1Requests, temporary, -1);
                phase1.Start();
                try
                {
                    var coreLoader = CreateLoader(coreWorkspace, options);
                    var coreInfo = await coreLoader.LoadSolutionInfoAsync(filter,
                        progress: phase1, msbuildLogger: ExperimentLoadSettings.Logger(options, "phase1"), cancellationToken: timeout.Token);
                    phase1SolutionDirectory = coreLoader.Properties.GetValueOrDefault("SolutionDir");
                    core = coreInfo.Projects.ToArray();
                    coreSolution = coreWorkspace.AddSolution(coreInfo);
                }
                finally
                {
                    phase1.Stop();
                }
            }

            var groupWorkspaces = Enumerable.Range(0, options.Workers).Select(_ => CreateWorkspace(diagnostics)).ToArray();
            workspaces.AddRange(groupWorkspaces);
            // A separate map and loader per group. Task.WhenAll settles every BuildHost before any workspace is disposed.
            groups = Enumerable.Range(0, options.Workers).Select(index => new CoreFirstGroup(
                index, aggregatorPaths[index], [], new PhaseLoadProgress(timeProvider, origin: start))).ToArray();
            Task Load(CoreFirstGroup group) => LoadGroupAsync(
                group, groupWorkspaces[group.Index], coreSolution, plan.Roots[group.Index], expectedSolutionDirectory, options, timeout.Token);
            if (options.Phase2 == "isolated")
            {
                foreach (var group in groups)
                {
                    await Load(group);
                }
            }
            else
            {
                await Task.WhenAll(groups.Select(Load));
            }
            violations = CoreFirstMerger.FindViolations(core, groups);
            var failedGroups = groups.Where(group => group.Status is "failed" or "timeout").ToArray();
            if (failedGroups.Length > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    failedGroups.Select(group => $"Group {group.Index} {group.Status}: {group.Error}")));
            }
            var mergeStart = timeProvider.GetTimestamp();
            var mergedInfo = CoreFirstMerger.Merge(core, groups, options.SolutionPath);
            var mergedWorkspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
            workspaces.Add(mergedWorkspace);
            var solution = mergedWorkspace.AddSolution(mergedInfo);
            unresolved = mergedInfo.Projects.Sum(project => project.ProjectReferences.Count(reference =>
                !solution.ContainsProject(reference.ProjectId)));
            mergeMs = timeProvider.GetElapsedTime(mergeStart).TotalMilliseconds;
            var wallMs = timeProvider.GetElapsedTime(start).TotalMilliseconds;
            var processMetrics = await finalization.StopAsync(wallMs, process.TotalProcessorTime.TotalMilliseconds - initialCpuMs);
            if (finalization.Error is not null)
            {
                throw new InvalidOperationException($"Sampler finalization failed: {finalization.Error}");
            }
            observation = SolutionObservation.Capture(solution);
            if (violations.Length > 0)
            {
                status = "plan-violation";
            }

            WriteReport(wallMs, processMetrics);
        }
        catch (Exception ex)
        {
            try
            {
                using var partialWorkspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
                var partialProjects = core.Concat(groups.SelectMany(group => group.Projects))
                    .DistinctBy(project => project.Id).ToArray();
                var partialInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(),
                    options.SolutionPath, partialProjects);
                observation = SolutionObservation.Capture(partialWorkspace.AddSolution(partialInfo));
            }
            catch (Exception captureError)
            {
                diagnostics.Enqueue("Partial observation unavailable: " + captureError);
            }
            if (recursive is not null)
            {
                violations = violations.Concat(recursive.Violations.Select(value => "Recursive core: " + value)).ToArray();
            }

            status = FailureEvidenceCapture.CoreFirstStatus(timeout.IsCancellationRequested, violations.Length > 0,
                options.Partition == "cluster" && (plan?.BalanceSatisfied == false || recursive?.Plan.BalanceSatisfied == false));
            var wallMs = timeProvider.GetElapsedTime(start).TotalMilliseconds;
            var evidence = await FailureEvidenceCapture.CaptureAsync(
                ex, finalization, wallMs, process.TotalProcessorTime.TotalMilliseconds - initialCpuMs);
            error = evidence.Error;
            WriteReport(wallMs, evidence.Process);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                workspace.Dispose();
            }

            sampler?.Dispose();
            Directory.Delete(temporary, recursive: true);
        }

        Console.WriteLine($"core-first graph={options.Graph} workers={options.Workers} aggregator={options.Aggregator} status={status} report={options.OutputPath}");
        return status == "ok" ? 0 : 2;

        void WriteReport(double wallMs, object processMetrics)
        {
            var report = new
            {
                Status = status, Error = error, options.Mode, options.Workers, Strategy = "core-first", options.Graph,
                options.GlobalProperties, Properties = ExperimentLoadSettings.Properties(options),
                LocalSdk = options.LocalSdkPath,
                DotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                options.Aggregator, Solution = options.SolutionPath, Registration = registration,
                options.Partition, options.BalanceEpsilon, options.CoreDepth, options.Phase2,
                FilterLocation = "temporary directory; absolute solution path", WallMs = wallMs,
                OriginalSolutionDirectory = expectedSolutionDirectory,
                Phase1SolutionDirectory = phase1SolutionDirectory,
                GroupSolutionDirectory = expectedSolutionDirectory,
                Phase1RequestedPaths = phase1Requests,
                PlanningMs = planningMs, AggregatorPreparationMs = preparationMs,
                Phase1Ms = phase1.ElapsedMs, Phase1FirstProgressMs = phase1.FirstProgressMs,
                Phase2FirstProgressMs = groups.Select(group => group.Progress.FirstProgressMs).ToArray(),
                GroupMs = groups.Select(group => group.Progress.ElapsedMs).ToArray(), MergeMs = mergeMs,
                GroupOutcomes = groups.Select(group => new
                {
                    group.Index, group.Status, group.Error,
                    group.Progress.ElapsedMs, group.Progress.FirstProgressMs,
                    group.Progress.StartOffsetMs, group.Progress.EndOffsetMs,
                    ReturnedProjects = group.Projects.Select(project => project.FilePath).ToArray(),
                }).ToArray(),
                Phase1StartOffsetMs = phase1.StartOffsetMs, Phase1EndOffsetMs = phase1.EndOffsetMs,
                RecursiveCore = recursive is null ? null : new
                {
                    recursive.Plan, recursive.SolutionDirectory, recursive.MergeMs, recursive.RestoreLogs,
                    LowerCoreMs = recursive.LowerProgress.ElapsedMs,
                    LowerCoreFirstProgressMs = recursive.LowerProgress.FirstProgressMs,
                    recursive.LowerProgress.StartOffsetMs, recursive.LowerProgress.EndOffsetMs,
                    LowerLoadedPaths = recursive.LowerProgress.LoadedPaths,
                    recursive.Violations,
                    Groups = recursive.Groups.Select(group => new
                    {
                        group.Index, group.Status, group.Error, group.Progress.ElapsedMs, group.Progress.FirstProgressMs,
                        group.Progress.StartOffsetMs, group.Progress.EndOffsetMs,
                        ReturnedProjects = group.Projects.Select(project => project.FilePath).ToArray(),
                    }).ToArray(),
                },
                RequestedCounts = plan?.Roots.Select(paths => paths.Length).ToArray() ?? [],
                GroupProjectCounts = groups.Select(group => group.Projects.Length).ToArray(),
                TotalProjects = core.Length + groups.Sum(group => group.Projects.Count(project =>
                    !string.Equals(project.FilePath, group.AggregatorPath, StringComparison.OrdinalIgnoreCase))),
                UniqueProjects = observation.Count,
                RepeatedProjects = groups.SelectMany(group => group.Projects.Where(project =>
                    !string.Equals(project.FilePath, group.AggregatorPath, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(project => ProjectMerger.Key(project.FilePath, project.Name)).Sum(group => Math.Max(0, group.Count() - 1)),
                UnresolvedReferenceCount = unresolved, DiagnosticCount = diagnostics.Count,
                Diagnostics = diagnostics.Distinct().Order(StringComparer.Ordinal).ToArray(),
                BinlogCount = options.BinlogDirectory is null ? 0 : Directory.GetFiles(options.BinlogDirectory, "*.binlog").Length,
                BinlogBytes = options.BinlogDirectory is null ? 0 : Directory.GetFiles(options.BinlogDirectory, "*.binlog").Sum(path => new FileInfo(path).Length),
                DiagnosticRun = options.BinlogDirectory is not null,
                BinlogFiles = options.BinlogDirectory is null ? [] : Directory.GetFiles(options.BinlogDirectory, "*.binlog")
                    .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray(),
                ProjectLoadProgress = new
                {
                    Phase1 = phase1.Events,
                    Groups = groups.Select(group => new { group.Index, group.Progress.Events }).ToArray(),
                },
                NativeProgress = phase1.Events.Concat(groups.SelectMany(group => group.Progress.Events))
                    .Where(value => value.Contains(".vcxproj", StringComparison.OrdinalIgnoreCase)).ToArray(),
                NativeFailures = Array.Empty<string>(),
                Process = processMetrics, Projects = observation, Plan = plan,
                SamplerFinalizationError = finalization.Error,
                PlanViolationCount = violations.Length, PlanViolations = violations,
                CoreReturnedFromGroups = groups.Select(group => group.Projects.Count(project => core.Any(item => item.Id == project.Id))).ToArray(),
                Phase1LoadedPaths = phase1.LoadedPaths,
                ActualCorePaths = core.Select(project => project.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                AdditionalCorePaths = core.Select(project => project.FilePath!).Except(plan?.Core ?? [], StringComparer.OrdinalIgnoreCase).ToArray(),
                Phase2LoadedPaths = groups.Select(group => group.Progress.LoadedPaths).ToArray(),
                AggregatorRestoreLogs = restoreLogs,
                FirstProgressMeaning = "Elapsed time to first public progress callback; includes first evaluation/build work, not a direct process-start timestamp.",
                HostAttributionMeaning = "Host observation lifetimes and load phase/group offsets share the sampler/run wall origin approximately (sampler readiness precedes run start). Overlap can narrow attribution; simultaneous group hosts cannot be assigned uniquely using public progress alone.",
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.OutputPath, LoadExperiment.Redact(json, options.SolutionPath, temporary));
        }
    }

    private static async Task<SolutionInfo> LoadRecursiveCoreAsync(
        RunOptions options,
        RecursiveCoreExecution execution,
        string[] unloadableRequests,
        string temporary,
        ConcurrentQueue<string> diagnostics,
        List<AdhocWorkspace> workspaces,
        PhaseLoadProgress parentProgress,
        TimeProvider timeProvider,
        long runStart,
        string registration,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(temporary);
        var lowerWorkspace = CreateWorkspace(diagnostics);
        workspaces.Add(lowerWorkspace);
        var requests = execution.Plan.Core.Concat(unloadableRequests).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ProjectInfo[] lower = [];
        var lowerSolution = lowerWorkspace.CurrentSolution;
        if (requests.Length > 0)
        {
            execution.LowerProgress.Start();
            try
            {
                var loader = CreateLoader(lowerWorkspace, options);
                var filter = ProjectPartition.WriteFilter(options.SolutionPath, requests, temporary, -2);
                var lowerInfo = await loader.LoadSolutionInfoAsync(filter,
                    progress: execution.LowerProgress, msbuildLogger: ExperimentLoadSettings.Logger(options, "recursive-phase1"), cancellationToken: cancellationToken);
                execution.SolutionDirectory = loader.Properties.GetValueOrDefault("SolutionDir");
                lower = lowerInfo.Projects.ToArray();
                lowerSolution = lowerWorkspace.AddSolution(lowerInfo);
            }
            finally
            {
                execution.LowerProgress.Stop();
            }
        }

        var paths = new string?[options.Workers];
        for (var index = 0; index < paths.Length; index++)
        {
            if (execution.Plan.Roots[index].Length > 0 && options.Aggregator != "sequential")
            {
                paths[index] = CoreFirstAggregator.Write(options.SolutionPath, temporary, index, execution.Plan.Roots[index]);
                if (options.Aggregator == "restore")
                {
                    execution.RestoreLogs.Add(await CoreFirstAggregator.RestoreAsync(paths[index]!, registration, cancellationToken));
                }
            }
        }

        var groupWorkspaces = Enumerable.Range(0, options.Workers).Select(_ => CreateWorkspace(diagnostics)).ToArray();
        workspaces.AddRange(groupWorkspaces);
        execution.Groups = Enumerable.Range(0, options.Workers).Select(index => new CoreFirstGroup(
            index, paths[index], [], new PhaseLoadProgress(timeProvider, parentProgress, runStart))).ToArray();
        var solutionDirectory = CoreFirstLoadInputs.SolutionDirectory(options.SolutionPath);
        await Task.WhenAll(execution.Groups.Select(group => LoadGroupAsync(
            group, groupWorkspaces[group.Index], lowerSolution, execution.Plan.Roots[group.Index], solutionDirectory, options, cancellationToken)));
        execution.Violations = CoreFirstMerger.FindViolations(lower, execution.Groups);
        var failed = execution.Groups.Where(group => group.Status is "failed" or "timeout").ToArray();
        if (failed.Length > 0 || execution.Violations.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                execution.Violations.Concat(failed.Select(group => $"Recursive group {group.Index} {group.Status}: {group.Error}"))));
        }

        var mergeStart = timeProvider.GetTimestamp();
        var result = CoreFirstMerger.Merge(lower, execution.Groups, options.SolutionPath);
        execution.MergeMs = timeProvider.GetElapsedTime(mergeStart).TotalMilliseconds;
        return result;
    }

    private static AdhocWorkspace CreateWorkspace(ConcurrentQueue<string> diagnostics)
    {
        var workspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
        workspace.RegisterWorkspaceFailedHandler(args => diagnostics.Enqueue(JsonSerializer.Serialize(new
        {
            Kind = args.Diagnostic.Kind.ToString(), args.Diagnostic.Message,
        })));
        return workspace;
    }

    private static MSBuildProjectLoader CreateLoader(AdhocWorkspace workspace, RunOptions options, string? solutionDirectory = null)
    {
        return new MSBuildProjectLoader(workspace, ExperimentLoadSettings.Properties(options, solutionDirectory));
    }

    private static async Task LoadGroupAsync(
        CoreFirstGroup group,
        AdhocWorkspace workspace,
        Solution coreSolution,
        string[] roots,
        string solutionDirectory,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        if (roots.Length == 0)
        {
            group.Status = "empty";
            return;
        }

        var map = ProjectMap.Create(coreSolution);
        var loader = CreateLoader(workspace, options, solutionDirectory);
        var projects = new Dictionary<ProjectId, ProjectInfo>();
        await CoreFirstGroupExecution.RunAsync(group, async () =>
        {
            foreach (var root in group.AggregatorPath is null ? roots : [group.AggregatorPath])
            {
                var loaded = await loader.LoadProjectInfoAsync(root, map, progress: group.Progress,
                    msbuildLogger: ExperimentLoadSettings.Logger(options, "group-" + group.Index), cancellationToken: cancellationToken);
                foreach (var project in loaded)
                {
                    projects.TryAdd(project.Id, project);
                }

                group.Projects = projects.Values.ToArray();
            }
        }, cancellationToken);
    }

    private sealed class RecursiveCoreExecution(CoreFirstPlan plan, PhaseLoadProgress lowerProgress)
    {
        public CoreFirstPlan Plan { get; } = plan;
        public PhaseLoadProgress LowerProgress { get; } = lowerProgress;
        public CoreFirstGroup[] Groups { get; set; } = [];
        public string[] Violations { get; set; } = [];
        public string? SolutionDirectory { get; set; }
        public double MergeMs { get; set; }
        public List<string> RestoreLogs { get; } = [];
    }
}
