using System.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class DotNetCliRunnerHangTests
{
    [Fact]
    public void FormatHangHints_mentions_timeout_and_the_platform_process_command()
    {
        var text = DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false);
        Assert.Contains("MCP_DOTNET_TIMEOUT", text, StringComparison.Ordinal);
        Assert.Contains(OperatingSystem.IsWindows() ? "`Get-Process dotnet`" : "`ps -ef`", text, StringComparison.Ordinal);
        Assert.DoesNotContain(OperatingSystem.IsWindows() ? "`ps -ef`" : "`Get-Process dotnet`", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausting_the_budget_during_sdk_resolution_skips_the_cli_command()
    {
        var timeout = TimeSpan.FromTicks(1);
        var workDir = Environment.CurrentDirectory;

        var combined = await DotNetCliRunner.RunWithMetadataAsync(
            "--version", workDir, CancellationToken.None, timeout);
        var separated = await DotNetCliRunner.RunSeparatedAsync("--version", workDir, timeout);

        Assert.True(combined.TimedOut);
        Assert.False(combined.ProcessKilled);
        Assert.Equal(-1, combined.ExitCode);
        Assert.Contains("Command start:** skipped", combined.RunMetadata, StringComparison.Ordinal);
        Assert.True(separated.TimedOut);
        Assert.Equal(-1, separated.ExitCode);
        Assert.Contains("Command start:** skipped", separated.RunMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithMetadataAsync_times_out_and_kills_long_running_process()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpHang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csproj = MsBuildDelayFixture.WriteHangProject(root);

            var elapsed = Stopwatch.StartNew();
            var run = await DotNetCliRunner.RunWithMetadataAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                CancellationToken.None,
                TimeSpan.FromSeconds(4));
            elapsed.Stop();

            Assert.True(run.TimedOut, run.CombinedOutput);
            Assert.True(run.ProcessKilled);
            Assert.True(
                elapsed.Elapsed < TimeSpan.FromSeconds(20),
                "CLI timeout must return after Kill, not hang on redirected pipes. elapsed=" + elapsed.Elapsed);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exited_parent_with_inherited_pipes_times_out(bool separated)
    {
        await using var fixture = new DotNetPipeFixture();
        using var cancellation = new CancellationTokenSource();
        var timeout = TimeSpan.FromSeconds(6);
        var run = CheckPipeTimeoutAsync(fixture, separated, timeout, cancellation.Token)
            .WaitAsync(timeout + TimeSpan.FromSeconds(5));

        try
        {
            await fixture.WaitForExitedParentAsync();
            await run;
            await fixture.WaitForExitedParentAsync();
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_is_observed_after_the_parent_exits(bool separated)
    {
        await using var fixture = new DotNetPipeFixture();
        using var cancellation = new CancellationTokenSource();
        Task run = separated
            ? DotNetCliRunner.RunSeparatedAsync(
                fixture.HoldPipesCommand,
                fixture.WorkingDirectory,
                cancellationToken: cancellation.Token)
            : DotNetCliRunner.RunWithMetadataAsync(fixture.HoldPipesCommand, fixture.WorkingDirectory, cancellation.Token);

        try
        {
            await fixture.WaitForExitedParentAsync();
            Assert.False(run.IsCompleted, "The runner should still be waiting for the child to close the pipes.");

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(8)));
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_exit_preserves_exit_code_and_both_outputs(bool separated)
    {
        await using var fixture = new DotNetPipeFixture();
        var timeout = TimeSpan.FromSeconds(10);
        if (separated)
        {
            var split = await DotNetCliRunner.RunSeparatedAsync(fixture.NormalCommand, fixture.WorkingDirectory, timeout);
            Assert.False(split.TimedOut);
            Assert.Null(split.ExceptionType);
            Assert.Equal(17, split.ExitCode);
            Assert.Equal("parent-stdout", split.StdOut);
            Assert.Equal("parent-stderr", split.StdErr);
            return;
        }

        var combined = await DotNetCliRunner.RunWithMetadataAsync(
            fixture.NormalCommand,
            fixture.WorkingDirectory,
            CancellationToken.None,
            timeout);
        Assert.False(combined.TimedOut);
        Assert.Equal(17, combined.ExitCode);
        Assert.Equal("parent-stdout" + Environment.NewLine + "parent-stderr", combined.CombinedOutput);
        Assert.Equal("parent-stdout".Length, combined.StdOutLength);
        Assert.Equal("parent-stderr".Length, combined.StdErrLength);
    }

    private static async Task CheckPipeTimeoutAsync(
        DotNetPipeFixture fixture,
        bool separated,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (separated)
        {
            var split = await DotNetCliRunner.RunSeparatedAsync(
                fixture.HoldPipesCommand,
                fixture.WorkingDirectory,
                timeout,
                cancellationToken);
            Assert.Null(split.ExceptionType);
            Assert.True(split.TimedOut);
            Assert.Equal(-1, split.ExitCode);
            Assert.True(split.OutputTruncated);
            Assert.InRange(split.StdOut.Length, 1, BoundedProcessOutput.MaxStreamChars);
            Assert.EndsWith("timeout-stdout", split.StdOut, StringComparison.Ordinal);
            Assert.Equal("timeout-stderr", split.StdErr);
            return;
        }

        var combined = await DotNetCliRunner.RunWithMetadataAsync(
            fixture.HoldPipesCommand,
            fixture.WorkingDirectory,
            cancellationToken,
            timeout);
        Assert.True(combined.TimedOut);
        Assert.False(combined.ProcessKilled);
        Assert.Equal(-1, combined.ExitCode);
        Assert.True(combined.OutputTruncated);
        Assert.Equal(250_000 + Environment.NewLine.Length + "timeout-stdout".Length, combined.StdOutLength);
        Assert.Equal("timeout-stderr".Length, combined.StdErrLength);
        Assert.EndsWith(
            "timeout-stdout" + Environment.NewLine + "timeout-stderr",
            combined.CombinedOutput,
            StringComparison.Ordinal);
    }
}
