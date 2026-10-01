using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Shared DLL-route step for the three test tools. One <see cref="SessionBuildState"/>
/// is held by the workspace manager; this type only reads and records it.
/// The CLI runner is awaited without the workspace semaphore. The caller must not hold that semaphore.
/// </summary>
internal sealed class TestDllEnsure
{
    public const string ReasonUnknown = "unknown";

    public const string ReasonDirty = "dirty";

    public const string ReasonContextChanged = "context changed";

    public const string ReasonOutputMissing = "output missing";

    public const string ReasonCoverageUnknown = "coverage unknown";

    public const string ReasonConfirmedReuse = "confirmed reuse";

    public const string NeverRefused =
        "Error: `never` runs tests only when the session build is current and the output identity matches. Tests were not started.";

    private readonly SessionBuildState _state;
    private readonly Runner _run;
    private readonly ICliProgressReporter? _progress;

    public TestDllEnsure(SessionBuildState state, Runner run, ICliProgressReporter? progress = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(run);
        _state = state;
        _run = run;
        _progress = progress;
    }

    public async Task<TestDllEnsureResult> ExecuteAsync(
        TestDllEnsureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ProjectId);
        ArgumentNullException.ThrowIfNull(request.Context);
        ArgumentNullException.ThrowIfNull(request.PullSnapshot);
        ArgumentNullException.ThrowIfNull(request.AssemblyExists);

        if (!string.IsNullOrWhiteSpace(request.MappingError))
        {
            return Refuse(request.MappingError, ReasonUnknown, request.AssemblyPath, solutionTarget: null);
        }

        var target = SolutionProjectTargetResolver.TryResolve(request.SolutionPath, request.ProjectNeedle);
        if (!target.Success || string.IsNullOrWhiteSpace(target.TargetName))
        {
            return Refuse(
                target.ErrorMessage ?? "Error: could not resolve the solution build target.",
                ReasonUnknown,
                request.AssemblyPath,
                solutionTarget: null);
        }

        var started = Stopwatch.StartNew();
        var snapshot = request.PullSnapshot();
        _state.Observe(snapshot);
        var outputExists = SafeExists(request);
        var decision = _state.EvaluateReuse(request.ProjectId, request.Context);
        var reason = Explain(request, decision, outputExists);
        // Skip only when the pull can prove the input bytes. Today's snapshot has no
        // content hash, so EvaluateReuse does not report current and this branch does not run.
        if (reason == ReasonConfirmedReuse && request.Policy != TestBuildPolicy.Always)
        {
            return await RunTestsAsync(request, target.TargetName, reason, started, previousExit: null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (request.Policy == TestBuildPolicy.Never)
        {
            return Refuse(NeverRefused, reason, request.AssemblyPath, target.TargetName);
        }

        return await BuildThenTestAsync(request, snapshot, target.TargetName, reason, started, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TestDllEnsureResult> BuildThenTestAsync(
        TestDllEnsureRequest request,
        WorkspaceInputSnapshot snapshot,
        string solutionTarget,
        string reason,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        string buildArguments;
        try
        {
            buildArguments = DotNetTestArguments.BuildPreTestBuild(
                request.SolutionPath,
                request.NoRestore,
                request.Context.Configuration,
                request.Context.Platform,
                request.Context.BuildArgs,
                solutionTarget,
                request.Context.TargetFramework);
        }
        catch (ArgumentException ex)
        {
            return Refuse($"Error: {ex.Message}", reason, request.AssemblyPath, solutionTarget);
        }

        var buildTimeout = DotNetTestArguments.RemainingTimeout(request.Timeout, started.Elapsed);
        if (buildTimeout == TimeSpan.Zero)
        {
            return StopForBudget(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun: null);
        }

        if (!_state.BeginBuild(request.ProjectId, request.Context, snapshot))
        {
            return Refuse(
                "Error: the solution build was not started because the input snapshot generation did not match. Tests were not started.",
                reason,
                request.AssemblyPath,
                solutionTarget);
        }

        CliProgressStep.ReportStarted(_progress, "ensure build (" + reason + ")");
        DotNetCliRunner.RunResult buildRun;
        try
        {
            buildRun = await _run(
                    buildArguments,
                    request.BuildWorkingDirectory,
                    buildTimeout,
                    previousExit: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                snapshot,
                succeeded: false,
                cancelled: true,
                outputIdentity: null);
            return new TestDllEnsureResult
            {
                Reason = reason,
                SolutionTarget = solutionTarget,
                AssemblyPath = request.AssemblyPath,
                BuildArguments = buildArguments,
                Cancelled = true,
                Error = "`dotnet test` was cancelled."
                    + Environment.NewLine
                    + Environment.NewLine
                    + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true),
            };
        }

        if (buildRun.TimedOut || buildRun.Cancelled)
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                snapshot,
                succeeded: false,
                cancelled: true,
                outputIdentity: null);
            return StopForBudget(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun);
        }

        if (buildRun.ExitCode != 0)
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                snapshot,
                succeeded: false,
                cancelled: false,
                outputIdentity: null);
            return new TestDllEnsureResult
            {
                Reason = reason,
                SolutionTarget = solutionTarget,
                AssemblyPath = request.AssemblyPath,
                BuildArguments = buildArguments,
                BuildRun = buildRun,
                Error = "Tests were not started because `dotnet build` failed.",
            };
        }

        if (!SafeExists(request))
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                snapshot,
                succeeded: true,
                cancelled: false,
                outputIdentity: null);
            return Refuse(
                TestAssemblyPathResolver.FormatAssemblyNotFound(request.AssemblyPath, afterSolutionTargetBuild: true),
                ReasonOutputMissing,
                request.AssemblyPath,
                solutionTarget,
                buildArguments,
                buildRun);
        }

        var after = request.PullSnapshot();
        _state.Observe(after);
        var identity = request.Context.OutputPath ?? Path.GetFullPath(request.AssemblyPath);
        var recorded = _state.TryRecordBuildResult(
            request.ProjectId,
            request.Context,
            after,
            succeeded: true,
            cancelled: false,
            identity);
        if (!recorded)
        {
            return Refuse(
                "Error: the solution build finished, but the session inputs changed before tests could start. Tests were not started.",
                reason,
                request.AssemblyPath,
                solutionTarget,
                buildArguments,
                buildRun);
        }

        return await RunTestsAsync(request, solutionTarget, reason, started, buildRun.ExitCode, cancellationToken, buildArguments, buildRun)
            .ConfigureAwait(false);
    }

    private async Task<TestDllEnsureResult> RunTestsAsync(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        Stopwatch started,
        int? previousExit,
        CancellationToken cancellationToken,
        string? buildArguments = null,
        DotNetCliRunner.RunResult? buildRun = null)
    {
        var testTimeout = DotNetTestArguments.RemainingTimeout(request.Timeout, started.Elapsed);
        if (testTimeout == TimeSpan.Zero)
        {
            return StopForBudget(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun);
        }

        string testArguments;
        try
        {
            // noRestore on this process would be a second restore. The caller's flag
            // is applied only to the solution pre-build. The test process does not restore.
            testArguments = DotNetTestArguments.Build(
                request.SolutionPath,
                request.Filter,
                noBuild: true,
                noRestore: true,
                request.Context.Configuration,
                request.Context.Platform,
                request.AssemblyPath);
        }
        catch (ArgumentException ex)
        {
            return Refuse($"Error: {ex.Message}", reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun);
        }

        try
        {
            var testRun = await _run(
                    testArguments,
                    request.TestWorkingDirectory,
                    testTimeout,
                    previousExit,
                    cancellationToken)
                .ConfigureAwait(false);
            return new TestDllEnsureResult
            {
                TestsStarted = true,
                Reason = reason,
                SolutionTarget = solutionTarget,
                AssemblyPath = request.AssemblyPath,
                BuildArguments = buildArguments,
                TestArguments = testArguments,
                BuildRun = buildRun,
                TestRun = testRun,
                TimedOut = testRun.TimedOut,
                Cancelled = testRun.Cancelled,
            };
        }
        catch (OperationCanceledException)
        {
            return new TestDllEnsureResult
            {
                Reason = reason,
                SolutionTarget = solutionTarget,
                AssemblyPath = request.AssemblyPath,
                BuildArguments = buildArguments,
                TestArguments = testArguments,
                BuildRun = buildRun,
                Cancelled = true,
                Error = "`dotnet test` was cancelled."
                    + Environment.NewLine
                    + Environment.NewLine
                    + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true),
            };
        }
    }

    private string Explain(TestDllEnsureRequest request, BuildReuseDecision decision, bool outputExists)
    {
        if (!outputExists)
        {
            return ReasonOutputMissing;
        }

        if (decision.State == BuildFreshnessState.Dirty)
        {
            return ReasonDirty;
        }

        if (decision.SkipAllowed
            && decision.State == BuildFreshnessState.Current
            && OutputIdentityMatches(request))
        {
            return ReasonConfirmedReuse;
        }

        if (decision.Blockers.Contains(BuildReuseBlocker.CoverageUnknown))
        {
            return ReasonCoverageUnknown;
        }

        if (decision.Blockers.Contains(BuildReuseBlocker.NoBuildProof)
            && _state.HasProofForDifferentContext(request.ProjectId, request.Context))
        {
            return ReasonContextChanged;
        }

        return ReasonUnknown;
    }

    private bool OutputIdentityMatches(TestDllEnsureRequest request)
    {
        var proof = _state.GetProof(request.ProjectId, request.Context);
        if (proof is null || string.IsNullOrWhiteSpace(proof.OutputIdentity))
        {
            return false;
        }

        var recorded = InputPathCanon.TryCanonicalize(proof.OutputIdentity) ?? proof.OutputIdentity;
        var candidate = InputPathCanon.TryCanonicalize(request.AssemblyPath) ?? request.AssemblyPath;
        return InputPathCanon.Comparer.Equals(recorded, candidate);
    }

    private static bool SafeExists(TestDllEnsureRequest request)
    {
        try
        {
            return request.AssemblyExists(request.AssemblyPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static TestDllEnsureResult Refuse(
        string error,
        string reason,
        string? assemblyPath,
        string? solutionTarget,
        string? buildArguments = null,
        DotNetCliRunner.RunResult? buildRun = null)
    {
        return new TestDllEnsureResult
        {
            Error = error,
            Reason = reason,
            AssemblyPath = assemblyPath,
            SolutionTarget = solutionTarget,
            BuildArguments = buildArguments,
            BuildRun = buildRun,
        };
    }

    private static TestDllEnsureResult StopForBudget(
        string reason,
        string? assemblyPath,
        string? solutionTarget,
        string? buildArguments,
        DotNetCliRunner.RunResult? buildRun)
    {
        string error;
        if (buildRun is { TimedOut: true })
        {
            error = "Tests were not started because `dotnet build` exceeded the tool timeout.";
        }
        else if (buildRun is { ExitCode: 0, Cancelled: false })
        {
            error = "Pre-test `dotnet build` succeeded, but no time remained in `timeoutSeconds` to start `dotnet test`.";
        }
        else
        {
            error = "Tests were not started because no time remained in `timeoutSeconds`.";
        }

        return new TestDllEnsureResult
        {
            Reason = reason,
            AssemblyPath = assemblyPath,
            SolutionTarget = solutionTarget,
            BuildArguments = buildArguments,
            BuildRun = buildRun,
            TimedOut = true,
            Error = error,
        };
    }

    internal delegate Task<DotNetCliRunner.RunResult> Runner(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        int? previousExit,
        CancellationToken cancellationToken);
}
