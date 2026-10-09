using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class IsolatedRestoreBenchmarkTests
{
    [Fact]
    public async Task Attempt_requires_a_runner_directory()
    {
        var directory = Environment.GetEnvironmentVariable(IsolatedRestoreAttempt.DirectoryVariable);
        if (directory is null)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => IsolatedRestoreAttempt.RunAsync(null, CancellationToken.None));
            Assert.Contains("runner directory", error.Message, StringComparison.Ordinal);
            return;
        }

        // Register before the capture method is compiled. The suite path above does not load MSBuild.
        MsBuildBootstrapper.Register();
        // The benchmark runner is the only caller that sets the handoff. A suite run leaves it unset.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await IsolatedRestoreAttempt.RunAsync(directory, timeout.Token);
    }
}
