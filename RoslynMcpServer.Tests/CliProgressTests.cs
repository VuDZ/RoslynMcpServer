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

    [Fact]
    public async Task RunSeparatedAsync_without_a_watch_still_times_out_and_kills_the_process()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = WriteHangProject(root);

            var run = await DotNetCliRunner.RunSeparatedAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                TimeSpan.FromSeconds(4),
                CancellationToken.None);

            Assert.True(run.TimedOut);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    [Fact]
    public async Task RunSeparatedAsync_reports_heartbeats_while_the_process_is_alive()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = WriteHangProject(root);
            var reporter = new RecordingProgressReporter();
            var watch = new CliProgressWatch(reporter, CliProgressStep.RunStage)
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(250),
            };

            var run = await DotNetCliRunner.RunSeparatedAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                TimeSpan.FromSeconds(4),
                CancellationToken.None,
                watch);

            Assert.True(run.TimedOut);
            Assert.NotEmpty(reporter.Updates);
            Assert.All(reporter.Updates, update => Assert.Equal(CliProgressStep.RunStage, update.Stage));
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

    [Fact]
    public async Task Test_orchestration_reports_build_then_test_labels_with_previous_exit()
    {
        var reporter = new RecordingProgressReporter();
        var workDir = Environment.CurrentDirectory;

        var build = await CliProgressStep.RunWithMetadataAsync(
            "--info",
            workDir,
            CancellationToken.None,
            timeout: TimeSpan.FromSeconds(30),
            reporter,
            CliProgressStep.BuildStage);
        Assert.Equal(0, build.ExitCode);

        var test = await CliProgressStep.RunWithMetadataAsync(
            "--info",
            workDir,
            CancellationToken.None,
            timeout: TimeSpan.FromSeconds(30),
            reporter,
            CliProgressStep.TestStage,
            build.ExitCode);
        Assert.Equal(0, test.ExitCode);

        var updates = reporter.Updates.ToArray();
        var starts = updates.Where(update => update.Elapsed == TimeSpan.Zero).ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal(CliProgressStep.BuildStage, starts[0].Stage);
        Assert.Null(starts[0].LastExitCode);
        Assert.Equal(CliProgressStep.TestStage, starts[1].Stage);
        Assert.Equal(build.ExitCode, starts[1].LastExitCode);
        Assert.Contains("starting", starts[1].Describe(), StringComparison.Ordinal);
        Assert.Contains($"previous step exit {build.ExitCode}", starts[1].Describe(), StringComparison.Ordinal);
        Assert.All(
            updates,
            update =>
            {
                Assert.DoesNotContain("Passed", update.Describe(), StringComparison.Ordinal);
                Assert.DoesNotContain("Failed", update.Describe(), StringComparison.Ordinal);
                Assert.DoesNotContain(workDir, update.Describe(), StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public async Task Test_orchestration_without_pre_test_reports_only_dotnet_test()
    {
        var reporter = new RecordingProgressReporter();

        var test = await CliProgressStep.RunWithMetadataAsync(
            "--info",
            Environment.CurrentDirectory,
            CancellationToken.None,
            timeout: TimeSpan.FromSeconds(30),
            reporter,
            CliProgressStep.TestStage);

        Assert.Equal(0, test.ExitCode);
        var updates = reporter.Updates.ToArray();
        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.Equal(CliProgressStep.TestStage, update.Stage));
        Assert.DoesNotContain(updates, update => update.Stage == CliProgressStep.BuildStage);
        Assert.Null(updates[0].LastExitCode);
        Assert.Equal(TimeSpan.Zero, updates[0].Elapsed);
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
