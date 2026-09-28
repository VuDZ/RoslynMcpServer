using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Runtime check of the failure the static contract describes: an async entry that re-acquires its own
/// non-recursive lock through the synchronous entry. The operation runs in a separate process, the parent
/// bounds it from the outside and kills the process tree on expiry, so a hang cannot take the test run
/// with it. The correct source must return its result — a finished task is not the assertion — and the
/// compilable mutation must time out, leave no process behind and leave the host usable for the next run.
/// </summary>
public sealed class DeadlockWitnessTests
{
    [Fact]
    public async Task Correct_entry_returns_the_operation_result()
    {
        using var witness = WitnessAssembly.Compile(SanitizedEntryDemoSource.Baseline);
        using var host = await DeadlockWitnessClient.StartAsync();

        var outcome = await host.RunAsync(
            witness.Path,
            SanitizedEntryDemoSource.WitnessTypeName,
            SanitizedEntryDemoSource.WitnessMethodName,
            CorrectOperationTimeout);

        Assert.True(outcome.IsCompleted, outcome.Describe());
        Assert.Equal(SanitizedEntryDemoSource.ExpectedValue.ToString(CultureInfo.InvariantCulture), outcome.Output);
        Assert.True(await host.WaitForExitAsync(CorrectOperationTimeout), "the host must exit by itself after reporting the result");
        Assert.Equal(0, host.ExitCode);
    }

    [Fact]
    public async Task Reentrant_entry_times_out_kills_the_tree_and_does_not_hang_the_next_run()
    {
        // The mutation is the compilable source change the static corpus detects: the async entry calls the
        // public synchronous entry while it already holds the lock.
        using var mutation = WitnessAssembly.Compile(SanitizedEntryDemoSource.AsyncCallsPublicSync);
        int deadlockedHostId;
        using (var deadlockedHost = await DeadlockWitnessClient.StartAsync())
        {
            deadlockedHostId = deadlockedHost.HostProcessId;
            var outcome = await deadlockedHost.RunAsync(
                mutation.Path,
                SanitizedEntryDemoSource.WitnessTypeName,
                SanitizedEntryDemoSource.WitnessMethodName,
                DeadlockOperationTimeout);

            Assert.Equal(WitnessStatus.TimedOut, outcome.Status);
            Assert.True(deadlockedHost.HasExited, "the killed host must be reaped before the outcome is returned");
        }

        AssertProcessIsGone(deadlockedHostId);

        // A following operation in a fresh host must still succeed: nothing from the killed run survives.
        using var witness = WitnessAssembly.Compile(SanitizedEntryDemoSource.Baseline);
        using var host = await DeadlockWitnessClient.StartAsync();
        var afterKill = await host.RunAsync(
            witness.Path,
            SanitizedEntryDemoSource.WitnessTypeName,
            SanitizedEntryDemoSource.WitnessMethodName,
            CorrectOperationTimeout);

        Assert.True(afterKill.IsCompleted, afterKill.Describe());
        Assert.Equal(SanitizedEntryDemoSource.ExpectedValue.ToString(CultureInfo.InvariantCulture), afterKill.Output);
    }

    private static void AssertProcessIsGone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.Fail($"the witness host process {processId} survived the tree kill (state: {(process.HasExited ? "exited" : "running")})");
        }
        catch (ArgumentException)
        {
            // No process with this id: the tree kill completed.
        }
    }

    /// <summary>Bound for the correct operation: it only acquires a free semaphore and returns a value.</summary>
    private static readonly TimeSpan CorrectOperationTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bound for the mutation. It deadlocks deterministically on the first call, so the bound only has to
    /// absorb the cost of running it; it stays small to keep the suite fast.
    /// </summary>
    private static readonly TimeSpan DeadlockOperationTimeout = TimeSpan.FromSeconds(5);
}
