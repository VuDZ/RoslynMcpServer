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
}
