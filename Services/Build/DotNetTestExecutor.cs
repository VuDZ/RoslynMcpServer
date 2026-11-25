using System.Diagnostics;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Build;

internal sealed class DotNetTestExecutor
{
    internal DotNetTestExecutor(TestDllEnsure.Runner runner)
    {
        _runner = runner;
    }

    internal async Task<DotNetTestResult> ExecuteAsync(
        DotNetTestArguments.CliPlan plan,
        string workingDirectory,
        TimeSpan? timeout,
        bool timeoutPerProcess,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(TestProcessOutcome.NotRun);
        }

        DotNetCliRunner.RunResult? buildRun = null;
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (plan.PreTestBuildArguments is not null)
            {
                var build = await BuildAsync(plan.PreTestBuildArguments, workingDirectory, timeout, cancellationToken)
                    .ConfigureAwait(false);
                buildRun = build.BuildRun;
                if (build.Outcome != TestProcessOutcome.NotRun || buildRun is null)
                {
                    return build;
                }
                if (!timeoutPerProcess)
                {
                    timeout = DotNetTestArguments.RemainingTimeout(timeout, elapsed.Elapsed);
                    if (timeout == TimeSpan.Zero)
                    {
                        return new(TestProcessOutcome.TimedOut, buildRun);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await TestAsync(plan.TestArguments, workingDirectory, timeout, buildRun, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(TestProcessOutcome.Cancelled, buildRun);
        }
    }

    /// <summary>A successful build carries its result and leaves testing pending.</summary>
    internal async Task<DotNetTestResult> BuildAsync(
        string arguments, string workingDirectory, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(TestProcessOutcome.NotRun);
        }

        try
        {
            var run = await _runner(arguments, workingDirectory, timeout, null, cancellationToken).ConfigureAwait(false);
            var outcome = run.Cancelled ? TestProcessOutcome.Cancelled
                : run.TimedOut ? TestProcessOutcome.TimedOut
                : run.ExitCode != 0 ? TestProcessOutcome.BuildFailed
                : TestProcessOutcome.NotRun;
            return new(outcome, run);
        }
        catch (OperationCanceledException)
        {
            return new(TestProcessOutcome.Cancelled);
        }
    }

    internal async Task<DotNetTestResult> TestAsync(
        string arguments,
        string workingDirectory,
        TimeSpan? timeout,
        DotNetCliRunner.RunResult? buildRun,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(TestProcessOutcome.NotRun, buildRun);
        }

        try
        {
            var run = await _runner(arguments, workingDirectory, timeout, buildRun?.ExitCode, cancellationToken)
                .ConfigureAwait(false);
            var parsed = VstestOutputParser.Parse(
                run.CombinedOutput, run.ExitCode, run.TestSummaryTruncated, run.OutputTruncated);
            var outcome = run.Cancelled ? TestProcessOutcome.Cancelled
                : run.TimedOut ? TestProcessOutcome.TimedOut
                : run.ExitCode != 0 || parsed.RetainedSummaryReportsFailure ? TestProcessOutcome.Failed
                : parsed.SummaryTruncated ? TestProcessOutcome.Partial
                : parsed.Summary is { Total: > 0 } ? TestProcessOutcome.Passed
                : TestProcessOutcome.Failed;
            return new(outcome, buildRun, run, parsed);
        }
        catch (OperationCanceledException)
        {
            return new(TestProcessOutcome.Cancelled, buildRun);
        }
    }

    private readonly TestDllEnsure.Runner _runner;
}

