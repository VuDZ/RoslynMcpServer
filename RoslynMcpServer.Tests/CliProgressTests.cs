using System.Collections.Concurrent;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class CliProgressTests
{
    [Fact]
    public void Describe_carries_stage_elapsed_and_previous_exit_without_stdout()
    {
        var update = new CliProgressUpdate("dotnet build -v:minimal --no-incremental", TimeSpan.FromSeconds(42), 1);

        var text = update.Describe();

        Assert.Contains("dotnet build -v:minimal --no-incremental", text, StringComparison.Ordinal);
        Assert.Contains("42s elapsed", text, StringComparison.Ordinal);
        Assert.Contains("previous step exit 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_omits_previous_exit_before_any_step_finished()
    {
        var text = new CliProgressUpdate("dotnet restore -v:minimal", TimeSpan.FromSeconds(3), null).Describe();

        Assert.Contains("3s elapsed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("previous step", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithMetadataAsync_without_a_watch_still_times_out_and_kills_the_process()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = WriteHangProject(root);

            var run = await DotNetCliRunner.RunWithMetadataAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                CancellationToken.None,
                TimeSpan.FromSeconds(4));

            Assert.True(run.TimedOut);
            Assert.True(run.ProcessKilled);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    [Fact]
    public async Task RunWithMetadataAsync_reports_heartbeats_while_the_process_is_alive()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = WriteHangProject(root);
            var reporter = new RecordingProgressReporter();
            var watch = new CliProgressWatch(reporter, "dotnet build -v:minimal")
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(250),
            };

            var run = await DotNetCliRunner.RunWithMetadataAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                CancellationToken.None,
                TimeSpan.FromSeconds(4),
                watch);

            Assert.True(run.TimedOut);
            Assert.NotEmpty(reporter.Updates);
            Assert.All(reporter.Updates, update => Assert.Equal("dotnet build -v:minimal", update.Stage));
            Assert.All(reporter.Updates, update => Assert.True(update.Elapsed > TimeSpan.Zero));
            Assert.All(
                reporter.Updates,
                update => Assert.DoesNotContain("Pinging", update.Describe(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpProgress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteHangProject(string root)
    {
        var csproj = Path.Combine(root, "Hang.csproj");
        File.WriteAllText(csproj, OperatingSystem.IsWindows()
            ? """
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <Target Name="Build" />
                <Target Name="CoreCompile" />
                <Target Name="Hang" BeforeTargets="Build">
                  <Exec Command="ping -n 60 127.0.0.1" IgnoreExitCode="true" />
                </Target>
              </Project>
              """
            : """
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <Target Name="Build" />
                <Target Name="CoreCompile" />
                <Target Name="Hang" BeforeTargets="Build">
                  <Exec Command="sleep 60" IgnoreExitCode="true" />
                </Target>
              </Project>
              """);
        return csproj;
    }

    private static void DeleteQuietly(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }

    private sealed class RecordingProgressReporter : ICliProgressReporter
    {
        public ConcurrentQueue<CliProgressUpdate> Updates { get; } = new();

        public void Report(CliProgressUpdate update) => Updates.Enqueue(update);
    }
}
