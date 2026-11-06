using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class DotNetTestExecutorTests
{
    [Theory]
    [InlineData("passed", TestProcessOutcome.Passed, 2)]
    [InlineData("failed", TestProcessOutcome.Failed, 2)]
    [InlineData("build failed", TestProcessOutcome.BuildFailed, 1)]
    [InlineData("build timeout", TestProcessOutcome.TimedOut, 1)]
    [InlineData("test timeout", TestProcessOutcome.TimedOut, 2)]
    [InlineData("build cancel", TestProcessOutcome.Cancelled, 1)]
    [InlineData("test cancel", TestProcessOutcome.Cancelled, 2)]
    [InlineData("no matches", TestProcessOutcome.Failed, 2)]
    public async Task Execution_returns_an_unambiguous_outcome(string scenario, object expected, int calls)
    {
        var arguments = new List<string>();
        var executor = new DotNetTestExecutor((command, _, _, _, _) =>
        {
            arguments.Add(command);
            var build = command == "build";
            if (scenario == (build ? "build cancel" : "test cancel"))
            {
                throw new OperationCanceledException();
            }
            var timeout = scenario == (build ? "build timeout" : "test timeout");
            var failed = scenario == (build ? "build failed" : "failed");
            var output = build ? "error CS1001: Build diagnostic"
                : scenario == "no matches" ? "No test matches the given testcase filter"
                : "Failed! - Failed: 1, Passed: 2, Skipped: 0, Total: 3";
            if (scenario == "passed" && !build)
            {
                output = "Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3";
            }
            return Task.FromResult(new DotNetCliRunner.RunResult(
                failed || timeout ? 1 : 0, output, "metadata", output.Length, 0, TimedOut: timeout, ProcessKilled: timeout));
        });

        var result = await executor.ExecuteAsync(new("build", "test"), ".", TimeSpan.FromSeconds(5),
            timeoutPerProcess: true, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(calls, arguments.Count);
        if (scenario is "failed" or "passed" or "test timeout")
        {
            Assert.Equal(3, result.Parsed!.Summary!.Total);
        }
        if (scenario == "build failed")
        {
            Assert.Contains("CS1001", result.BuildRun!.CombinedOutput, StringComparison.Ordinal);
            Assert.Null(result.TestRun);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Timeout_is_per_process_only_when_requested(bool timeoutPerProcess)
    {
        var budgets = new List<TimeSpan?>();
        var executor = new DotNetTestExecutor(async (command, _, budget, _, _) =>
        {
            budgets.Add(budget);
            if (command == "build")
            {
                await Task.Delay(80);
            }
            return new DotNetCliRunner.RunResult(0, "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1", "", 0, 0);
        });
        var timeout = TimeSpan.FromSeconds(5);

        await executor.ExecuteAsync(new("build", "test"), ".", timeout, timeoutPerProcess, CancellationToken.None);

        Assert.Equal(2, budgets.Count);
        Assert.Equal(timeout, budgets[0]);
        if (timeoutPerProcess)
        {
            Assert.Equal(timeout, budgets[1]);
        }
        else
        {
            Assert.True(budgets[1] < timeout - TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public async Task Pre_cancelled_execution_starts_no_process()
    {
        var calls = 0;
        var executor = new DotNetTestExecutor((_, _, _, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("A cancelled operation must not start a process.");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(new("build", "test"), ".", null,
            timeoutPerProcess: true, cancellation.Token);

        Assert.Equal(TestProcessOutcome.NotRun, result.Outcome);
        Assert.Equal(0, calls);
    }
}
