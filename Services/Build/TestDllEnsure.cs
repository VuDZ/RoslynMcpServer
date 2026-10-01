using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Shared DLL-route step for the three test tools. One <see cref="SessionBuildState"/>
/// is held by the workspace manager; this type only reads and records it.
/// Callers in this process that share a build context run one at a time. That queue
/// does not stop a build started outside this process, and it is not held as a
/// workspace semaphore. The CLI runner is awaited without the workspace semaphore.
/// A generation check is only <see cref="TestDllEnsureRequest.PullSnapshot"/>: that
/// semaphore is not reentrant, so the check must not acquire it again.
/// One rebuild is allowed when inputs or the output move after a successful build.
/// Both builds and the test process share the caller's timeout.
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

    public const string RaceExhausted =
        "Error: inputs or the output changed again after one rebuild. Tests were not started.";

    public const string GenerationRejected =
        "Error: the session generation changed before tests could start. The earlier build is not proof for this session. Tests were not started.";

    public const string WaiterRejected =
        "Error: another operation on this build context failed or the session generation changed. That result is not proof for this call. Tests were not started.";

    public const string RebuildBudgetExhausted =
        "Tests were not started because no time remained in `timeoutSeconds` to rebuild after the inputs or the output changed.";

    public const string ContextBusy =
        "Tests were not started because no time remained in `timeoutSeconds` while another operation was using this build context.";

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
        var admission = await EnterAsync(request, started, cancellationToken).ConfigureAwait(false);
        if (!admission.Acquired)
        {
            return ContextBusyResult(request.AssemblyPath, target.TargetName);
        }

        TestDllEnsureResult? result = null;
        try
        {
            if (!WaiterMayContinue(request, admission))
            {
                result = Refuse(WaiterRejected, ReasonUnknown, request.AssemblyPath, target.TargetName);
                return result;
            }

            result = await RunEnsuredAsync(request, target.TargetName, started, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _state.LeaveContext(
                request.ProjectId,
                request.Context,
                confirmed: result?.TestsStarted == true,
                failed: result is null || result.FailedOperation,
                _state.CurrentGeneration ?? Guid.Empty);
        }
    }

    private async Task<ContextAdmission> EnterAsync(
        TestDllEnsureRequest request,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var wait = DotNetTestArguments.RemainingTimeout(request.Timeout, started.Elapsed);
        return await _state.EnterContextAsync(request.ProjectId, request.Context, wait, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool WaiterMayContinue(TestDllEnsureRequest request, ContextAdmission admission)
    {
        if (!admission.Waited)
        {
            return true;
        }

        // The pull is the generation check. It must not acquire the workspace semaphore.
        var now = request.PullSnapshot();
        _state.Observe(now);
        return !admission.PriorFailed && now.Generation == admission.PriorGeneration;
    }

    private async Task<TestDllEnsureResult> RunEnsuredAsync(
        TestDllEnsureRequest request,
        string solutionTarget,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var snapshot = request.PullSnapshot();
        _state.Observe(snapshot);
        var outputExists = SafeExists(request);
        var decision = _state.EvaluateReuse(request.ProjectId, request.Context);
        var reason = Explain(request, decision, outputExists);
        // Skip only when the pull can prove the input bytes. Today's snapshot has no
        // content hash, so EvaluateReuse does not report current and this branch does not run.
        if (reason == ReasonConfirmedReuse && request.Policy != TestBuildPolicy.Always)
        {
            return await RunTestsAsync(request, solutionTarget, reason, started, previousExit: null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (request.Policy == TestBuildPolicy.Never)
        {
            return Refuse(
                NeverRefused,
                reason,
                request.AssemblyPath,
                solutionTarget,
                failedOperation: false);
        }

        return await BuildThenTestAsync(request, solutionTarget, reason, started, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TestDllEnsureResult> BuildThenTestAsync(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var first = await BuildOnceAsync(request, solutionTarget, reason, started, rebuild: false, cancellationToken)
            .ConfigureAwait(false);
        if (first.Terminal is not null)
        {
            return first.Terminal;
        }

        if (first.ReadyForTests)
        {
            return await RunTestsAsync(
                    request,
                    solutionTarget,
                    reason,
                    started,
                    first.BuildRun?.ExitCode,
                    cancellationToken,
                    first.BuildArguments,
                    first.BuildRun)
                .ConfigureAwait(false);
        }

        if (DotNetTestArguments.RemainingTimeout(request.Timeout, started.Elapsed) == TimeSpan.Zero)
        {
            return StopBeforeRebuild(reason, request.AssemblyPath, solutionTarget, first.BuildArguments, first.BuildRun);
        }

        var second = await BuildOnceAsync(request, solutionTarget, reason, started, rebuild: true, cancellationToken)
            .ConfigureAwait(false);
        if (second.Terminal is not null)
        {
            return second.Terminal;
        }

        if (!second.ReadyForTests)
        {
            return Refuse(
                RaceExhausted,
                reason,
                request.AssemblyPath,
                solutionTarget,
                second.BuildArguments ?? first.BuildArguments,
                second.BuildRun ?? first.BuildRun);
        }

        return await RunTestsAsync(
                request,
                solutionTarget,
                reason,
                started,
                second.BuildRun?.ExitCode,
                cancellationToken,
                second.BuildArguments,
                second.BuildRun)
            .ConfigureAwait(false);
    }

    private async Task<BuildAttempt> BuildOnceAsync(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        Stopwatch started,
        bool rebuild,
        CancellationToken cancellationToken)
    {
        var launched = await LaunchBuildAsync(request, solutionTarget, reason, started, rebuild, cancellationToken)
            .ConfigureAwait(false);
        if (launched.Terminal is not null || launched.BuildRun is null || launched.Started is null)
        {
            return launched;
        }

        return ConfirmBuild(request, solutionTarget, reason, launched);
    }

    private async Task<BuildAttempt> LaunchBuildAsync(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        Stopwatch started,
        bool rebuild,
        CancellationToken cancellationToken)
    {
        var buildTimeout = DotNetTestArguments.RemainingTimeout(request.Timeout, started.Elapsed);
        if (buildTimeout == TimeSpan.Zero)
        {
            return BuildAttempt.Stop(StopForBudget(reason, request.AssemblyPath, solutionTarget, buildArguments: null, buildRun: null));
        }

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
            return BuildAttempt.Stop(Refuse($"Error: {ex.Message}", reason, request.AssemblyPath, solutionTarget));
        }

        var snapshot = request.PullSnapshot();
        _state.Observe(snapshot);
        if (!_state.BeginBuild(request.ProjectId, request.Context, snapshot))
        {
            return BuildAttempt.Stop(Refuse(
                "Error: the solution build was not started because the input snapshot generation did not match. Tests were not started.",
                reason,
                request.AssemblyPath,
                solutionTarget,
                buildArguments));
        }

        var label = rebuild ? "ensure rebuild (" + reason + ")" : "ensure build (" + reason + ")";
        CliProgressStep.ReportStarted(_progress, label);
        try
        {
            var buildRun = await _run(
                    buildArguments,
                    request.BuildWorkingDirectory,
                    buildTimeout,
                    previousExit: null,
                    cancellationToken)
                .ConfigureAwait(false);
            return new BuildAttempt
            {
                BuildArguments = buildArguments,
                BuildRun = buildRun,
                Started = snapshot,
                OutputStamp = OutputStamp(request),
            };
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
            return BuildAttempt.Stop(Cancelled(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun: null));
        }
    }

    private BuildAttempt ConfirmBuild(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        BuildAttempt launched)
    {
        var buildRun = launched.BuildRun!;
        var buildArguments = launched.BuildArguments;
        var started = launched.Started!;
        if (buildRun.TimedOut || buildRun.Cancelled)
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                started,
                succeeded: false,
                cancelled: true,
                outputIdentity: null);
            return BuildAttempt.Stop(StopForBudget(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun));
        }

        if (buildRun.ExitCode != 0)
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                started,
                succeeded: false,
                cancelled: false,
                outputIdentity: null);
            return BuildAttempt.Stop(new TestDllEnsureResult
            {
                Reason = reason,
                SolutionTarget = solutionTarget,
                AssemblyPath = request.AssemblyPath,
                BuildArguments = buildArguments,
                BuildRun = buildRun,
                FailedOperation = true,
                Error = "Tests were not started because `dotnet build` failed.",
            });
        }

        if (!SafeExists(request))
        {
            _state.TryRecordBuildResult(
                request.ProjectId,
                request.Context,
                started,
                succeeded: true,
                cancelled: false,
                outputIdentity: null);
            return BuildAttempt.Stop(Refuse(
                TestAssemblyPathResolver.FormatAssemblyNotFound(request.AssemblyPath, afterSolutionTargetBuild: true),
                ReasonOutputMissing,
                request.AssemblyPath,
                solutionTarget,
                buildArguments,
                buildRun));
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
            return Unconfirmed(request, solutionTarget, reason, buildArguments, buildRun, started, after);
        }

        return ConfirmStable(request, solutionTarget, reason, buildArguments, buildRun, started, launched.OutputStamp);
    }

    private BuildAttempt ConfirmStable(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        string? buildArguments,
        DotNetCliRunner.RunResult buildRun,
        WorkspaceInputSnapshot started,
        string? outputStamp)
    {
        // A generation change is not a rebuild. Observe before the output stamp so a new
        // generation is rejected even when the file changed in the same pull.
        var gate = request.PullSnapshot();
        _state.Observe(gate);
        if (_state.GetProof(request.ProjectId, request.Context) is null)
        {
            return Unconfirmed(request, solutionTarget, reason, buildArguments, buildRun, started, gate);
        }

        if (!string.Equals(outputStamp, OutputStamp(request), StringComparison.Ordinal))
        {
            return BuildAttempt.Again(buildArguments, buildRun);
        }

        return BuildAttempt.Ready(buildArguments, buildRun);
    }

    private static BuildAttempt Unconfirmed(
        TestDllEnsureRequest request,
        string solutionTarget,
        string reason,
        string? buildArguments,
        DotNetCliRunner.RunResult buildRun,
        WorkspaceInputSnapshot started,
        WorkspaceInputSnapshot now)
    {
        if (started.Generation != now.Generation || started.MembershipRevision != now.MembershipRevision)
        {
            return BuildAttempt.Stop(Refuse(
                GenerationRejected,
                reason,
                request.AssemblyPath,
                solutionTarget,
                buildArguments,
                buildRun));
        }

        return BuildAttempt.Again(buildArguments, buildRun);
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
                FailedOperation = testRun.TimedOut || testRun.Cancelled,
            };
        }
        catch (OperationCanceledException)
        {
            return Cancelled(reason, request.AssemblyPath, solutionTarget, buildArguments, buildRun, testArguments);
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

    private static string OutputStamp(TestDllEnsureRequest request)
    {
        if (!SafeExists(request))
        {
            return "missing";
        }

        try
        {
            var info = new FileInfo(request.AssemblyPath);
            if (!info.Exists)
            {
                return "probe";
            }

            return info.Length.ToString(CultureInfo.InvariantCulture)
                + ":"
                + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
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
        DotNetCliRunner.RunResult? buildRun = null,
        bool failedOperation = true)
    {
        return new TestDllEnsureResult
        {
            Error = error,
            Reason = reason,
            AssemblyPath = assemblyPath,
            SolutionTarget = solutionTarget,
            BuildArguments = buildArguments,
            BuildRun = buildRun,
            FailedOperation = failedOperation,
        };
    }

    private static TestDllEnsureResult Cancelled(
        string reason,
        string? assemblyPath,
        string? solutionTarget,
        string? buildArguments,
        DotNetCliRunner.RunResult? buildRun,
        string? testArguments = null)
    {
        return new TestDllEnsureResult
        {
            Reason = reason,
            SolutionTarget = solutionTarget,
            AssemblyPath = assemblyPath,
            BuildArguments = buildArguments,
            TestArguments = testArguments,
            BuildRun = buildRun,
            Cancelled = true,
            FailedOperation = true,
            Error = "`dotnet test` was cancelled."
                + Environment.NewLine
                + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true),
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
            FailedOperation = true,
            Error = error,
        };
    }

    private static TestDllEnsureResult StopBeforeRebuild(
        string reason,
        string? assemblyPath,
        string? solutionTarget,
        string? buildArguments,
        DotNetCliRunner.RunResult? buildRun)
    {
        return new TestDllEnsureResult
        {
            Reason = reason,
            AssemblyPath = assemblyPath,
            SolutionTarget = solutionTarget,
            BuildArguments = buildArguments,
            BuildRun = buildRun,
            TimedOut = true,
            FailedOperation = true,
            Error = RebuildBudgetExhausted,
        };
    }

    private static TestDllEnsureResult ContextBusyResult(string? assemblyPath, string? solutionTarget)
    {
        return new TestDllEnsureResult
        {
            Reason = ReasonUnknown,
            AssemblyPath = assemblyPath,
            SolutionTarget = solutionTarget,
            TimedOut = true,
            FailedOperation = true,
            Error = ContextBusy,
        };
    }

    private readonly struct BuildAttempt
    {
        public TestDllEnsureResult? Terminal { get; init; }

        public bool ReadyForTests { get; init; }

        public string? BuildArguments { get; init; }

        public DotNetCliRunner.RunResult? BuildRun { get; init; }

        public WorkspaceInputSnapshot? Started { get; init; }

        public string? OutputStamp { get; init; }

        public static BuildAttempt Stop(TestDllEnsureResult result)
        {
            return new BuildAttempt { Terminal = result };
        }

        public static BuildAttempt Again(string? buildArguments, DotNetCliRunner.RunResult buildRun)
        {
            return new BuildAttempt { BuildArguments = buildArguments, BuildRun = buildRun };
        }

        public static BuildAttempt Ready(string? buildArguments, DotNetCliRunner.RunResult buildRun)
        {
            return new BuildAttempt { ReadyForTests = true, BuildArguments = buildArguments, BuildRun = buildRun };
        }
    }

    internal delegate Task<DotNetCliRunner.RunResult> Runner(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        int? previousExit,
        CancellationToken cancellationToken);
}
