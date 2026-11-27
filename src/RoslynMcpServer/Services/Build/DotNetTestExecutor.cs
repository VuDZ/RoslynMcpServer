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
            return await TestWithTrxAsync(plan.TestArguments, workingDirectory, timeout, buildRun,
                plan.TestAssemblyPath, plan.Filter, cancellationToken)
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

    internal async Task<DotNetTestResult> TestWithTrxAsync(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        DotNetCliRunner.RunResult? buildRun,
        string? assemblyPath,
        string? filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("RoslynMcpVstest-");
        try
        {
            arguments += $" --logger trx --results-directory \"{QuoteContents(directory.FullName)}\"";
            if (assemblyPath is null)
            {
                var binlogPath = Path.Combine(directory.FullName, VstestTrxReader.BinlogName);
                arguments += $" -bl:\"{QuoteContents(binlogPath)};ProjectImports=None\"";
            }

            var run = await _runner(arguments, workingDirectory, timeout, buildRun?.ExitCode, cancellationToken)
                .ConfigureAwait(false);
            if (run.Cancelled || run.TimedOut)
            {
                return new(run.Cancelled ? TestProcessOutcome.Cancelled : TestProcessOutcome.TimedOut, buildRun, run);
            }

            VstestTrxResult results;
            try
            {
                results = VstestTrxReader.Read(directory.FullName, assemblyPath,
                    string.IsNullOrWhiteSpace(filter) ? null : filter, run, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
            {
                var outcome = run.ExitCode == 0 ? TestProcessOutcome.Partial : TestProcessOutcome.Failed;
                results = new(outcome, null, [], [], false, false, "The VSTest results could not be read.", null);
            }

            return new(results.Outcome, buildRun, run, Trx: results);
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // The test process may have removed its results directory.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not remove VSTest results directory '{0}': {1}", directory.FullName, ex.Message);
            }
        }
    }

    private static string QuoteContents(string path) => path.Replace("\"", "\\\"", StringComparison.Ordinal);

    private readonly TestDllEnsure.Runner _runner;
}

