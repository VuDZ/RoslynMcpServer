using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots;

namespace RoslynMcpServer.Services.Testing;

internal sealed class TestImpactRunner(
    AssemblySnapshotSession assemblies,
    DotNetTestExecutor executor,
    ILogger logger)
{
    internal async Task<(IReadOnlyDictionary<ProjectId, TestImpactProjectRun> Projects, int Degree)> RunAsync(
        TestImpactWorkspaceContext context,
        TestImpactEstimate estimate,
        TestClassStageLevel depth,
        int timeoutSeconds,
        int maxParallelProjects,
        CancellationToken cancellationToken)
    {
        var projects = estimate.Plans
            .Select(plan => new ProjectRun(plan, context.Solution.GetProject(plan.ProjectId)?.FilePath ?? ""))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ToArray();
        // Finding the test projects parses every document. Reuse the inventory across all narrowing calls in this run.
        var inputs = projects.Length == 0
            ? []
            : await AssemblySnapshotSession.InputsAsync(context.Solution, cancellationToken).ConfigureAwait(false);
        var request = new RunRequest(context, estimate, inputs, depth,
            timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null);

        int degree;
        if (maxParallelProjects == 1)
        {
            await RunSequentialAsync(projects, request, cancellationToken).ConfigureAwait(false);
            degree = projects.Any(project => project.TestArguments is not null) ? 1 : 0;
        }
        else
        {
            degree = await RunStagedAsync(projects, request, maxParallelProjects, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Rapid TIA run: {Outcomes}", string.Join("; ", projects.Select(project =>
            project.Path.Replace('\r', ' ').Replace('\n', ' ') + "=" + project.Execution.OutcomeText)));
        return (projects.ToDictionary(project => project.Plan.ProjectId,
            project => new TestImpactProjectRun(project.Execution, project.Analysis)), degree);
    }

    private async Task RunSequentialAsync(
        ProjectRun[] projects, RunRequest request, CancellationToken cancellationToken)
    {
        foreach (var project in projects)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            await BuildAsync(project, request, cancellationToken).ConfigureAwait(false);
            await PrepareAsync(project, request, cancellationToken).ConfigureAwait(false);
            if (project.TestArguments is not null)
            {
                await TestAsync(project, request.Timeout, cancellationToken).ConfigureAwait(false);
            }
            if (project.Execution.Outcome == TestProcessOutcome.Cancelled)
            {
                break;
            }
        }
    }

    private async Task<int> RunStagedAsync(
        ProjectRun[] projects, RunRequest request, int maximum, CancellationToken cancellationToken)
    {
        // A later build can overwrite a shared dependency. Finish every build before taking the test plans
        // from the resulting assemblies, and keep all test processes on those outputs.
        foreach (var project in projects)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            await BuildAsync(project, request, cancellationToken).ConfigureAwait(false);
        }
        foreach (var project in projects)
        {
            await PrepareAsync(project, request, cancellationToken).ConfigureAwait(false);
        }

        var ready = projects.Where(project => project.TestArguments is not null)
            .OrderByDescending(project => _lastTestTimes.GetValueOrDefault(project.Path))
            .ToArray();
        var degree = Math.Min(ready.Length, maximum == 0 ? Math.Max(1, Environment.ProcessorCount / 2) : maximum);
        var running = new List<Task>(degree);
        foreach (var project in ready)
        {
            if (running.Count == degree)
            {
                var completed = await Task.WhenAny(running).ConfigureAwait(false);
                running.Remove(completed);
                await completed.ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            // Invoke in order. The CLI runner starts the process before its first asynchronous wait.
            running.Add(TestAsync(project, request.Timeout, cancellationToken));
        }
        await Task.WhenAll(running).ConfigureAwait(false);
        return degree;
    }

    private async Task BuildAsync(ProjectRun project, RunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            project.WorkingDirectory = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(project.Path);
            project.Platform = DotNetConfigurationArguments.CoalescePlatformForTarget(
                null, request.Context.PlatformRaw, request.Context.Platform, project.Path);
            var arguments = DotNetTestArguments.BuildPreTestBuild(project.Path,
                configuration: request.Context.Configuration, platform: project.Platform, buildArgs: request.Context.BuildArgs);
            project.Execution = await executor.BuildAsync(arguments, project.WorkingDirectory, request.Timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            NoteFailure(project, exception);
        }
    }

    private async Task PrepareAsync(ProjectRun project, RunRequest request, CancellationToken cancellationToken)
    {
        if (project.Execution.Outcome != TestProcessOutcome.NotRun || project.Execution.BuildRun is null
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            project.Analysis = await TestClassNarrower.AnalyzeProjectAsync(request.Context.Solution, request.Estimate,
                assemblies, request.Inputs, project.Plan.ProjectId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Rapid TIA narrowing failed for {ProjectPath}", project.Path);
            project.Analysis = TestClassNarrower.Failed(project.Plan.ProjectId, exception);
        }

        var narrowing = project.Analysis.Find(project.Plan.ProjectId);
        var stage = narrowing?.SelectedStage(request.Depth);
        if (stage is { SelectsTests: false })
        {
            return;
        }

        project.TestFilter = narrowing is null ? project.Plan.Filter : stage?.Filter;
        project.TestArguments = DotNetTestArguments.Build(project.Path, project.TestFilter,
            noBuild: true, noRestore: true, configuration: request.Context.Configuration, platform: project.Platform);
    }

    private async Task TestAsync(ProjectRun project, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // The same TRX reader as the public test tools decides the outcome and owns the results directory.
            project.Execution = await executor.TestWithTrxAsync(project.TestArguments!, project.WorkingDirectory, timeout,
                project.Execution.BuildRun, assemblyPath: null, project.TestFilter, cancellationToken).ConfigureAwait(false);
            if (project.Execution.TestRun is { Elapsed: { } elapsed, Cancelled: false, TimedOut: false })
            {
                _lastTestTimes[project.Path] = elapsed;
            }
        }
        catch (OperationCanceledException)
        {
            // The TRX executor lets cancellation propagate; the run summary reports it as an outcome, not a failure.
            project.Execution = new(TestProcessOutcome.Cancelled, project.Execution.BuildRun);
        }
        catch (Exception exception)
        {
            NoteFailure(project, exception);
        }
    }

    private void NoteFailure(ProjectRun project, Exception exception)
    {
        logger.LogError(exception, "Affected test run failed for {ProjectPath}", project.Path);
        project.Execution = project.Execution with { Error = exception.Message };
    }

    private sealed record RunRequest(
        TestImpactWorkspaceContext Context,
        TestImpactEstimate Estimate,
        IReadOnlyList<IlAssemblyInput> Inputs,
        TestClassStageLevel Depth,
        TimeSpan? Timeout);

    private sealed class ProjectRun(VstestProjectRunPlan plan, string path)
    {
        public VstestProjectRunPlan Plan { get; } = plan;
        public string Path { get; } = path;
        public string WorkingDirectory { get; set; } = "";
        public string? Platform { get; set; }
        public DotNetTestResult Execution { get; set; } = new(TestProcessOutcome.NotRun);
        public TestNarrowingResult? Analysis { get; set; }
        public string? TestArguments { get; set; }
        public string? TestFilter { get; set; }
    }

    private readonly ConcurrentDictionary<string, TimeSpan> _lastTestTimes = new(InputPathCanon.Comparer);
}
