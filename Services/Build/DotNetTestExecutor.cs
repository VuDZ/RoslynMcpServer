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
                buildRun = await _runner(plan.PreTestBuildArguments, workingDirectory, timeout, null, cancellationToken)
                    .ConfigureAwait(false);
                if (buildRun.Cancelled)
                {
                    return new(TestProcessOutcome.Cancelled, buildRun);
                }
                if (buildRun.TimedOut)
                {
                    return new(TestProcessOutcome.TimedOut, buildRun);
                }
                if (buildRun.ExitCode != 0)
                {
                    return new(TestProcessOutcome.BuildFailed, buildRun);
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
            var run = await _runner(plan.TestArguments, workingDirectory, timeout, buildRun?.ExitCode, cancellationToken)
                .ConfigureAwait(false);
            var parsed = VstestOutputParser.Parse(run.CombinedOutput, run.ExitCode);
            var outcome = run.Cancelled ? TestProcessOutcome.Cancelled
                : run.TimedOut ? TestProcessOutcome.TimedOut
                : run.ExitCode != 0 || parsed.Summary is { Failed: > 0 } ? TestProcessOutcome.Failed
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

