using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

[Collection("DiagnosticReportStore")]
public sealed class BuildOutputIntegrationTests
{
    [Fact]
    public async Task Build_and_raw_build_share_silent_and_warning_modes()
    {
        using var project = new TempProject();
        var manager = SolutionManagerTestFactory.Create();
        var buildTools = new BuildTools(manager, NullLogger<BuildTools>.Instance);
        var utilityTools = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        var silent = await buildTools.RunDotNetBuild(project.Path, noIncremental: false);
        Assert.Collection(
            silent.Split(Environment.NewLine + Environment.NewLine, count: 2),
            report => Assert.Equal("## Build succeeded", report),
            guidance => Assert.StartsWith("Warning: Use the containing .sln or .slnx", guidance, StringComparison.Ordinal));
        var detailed = await buildTools.RunDotNetBuild(project.Path, noIncremental: false, includeBuildWarnings: true);
        Assert.Contains("MSB9001", detailed);
        Assert.DoesNotContain("Execution context", detailed);
        Assert.Equal("## Build succeeded", await utilityTools.ExecuteDotNetCommand($"build \"{project.Path}\""));
        var rawDetailed = await utilityTools.ExecuteDotNetCommand($"build \"{project.Path}\"", includeBuildWarnings: true);
        Assert.Contains("MSB9001", rawDetailed);
        Assert.DoesNotContain("Execution context", rawDetailed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Build_rejects_timeout_outside_supported_range_before_launch(int timeoutSeconds)
    {
        using var project = new TempProject();
        var buildTools = new BuildTools(SolutionManagerTestFactory.Create(), NullLogger<BuildTools>.Instance);

        var report = await buildTools.RunDotNetBuild(project.Path, timeoutSeconds: timeoutSeconds);

        Assert.Contains("Error: `timeoutSeconds` must be between 0 and", report, StringComparison.Ordinal);
        Assert.False(Directory.Exists(System.IO.Path.Combine(project.Root, "obj")));
    }

    [Fact]
    public async Task Build_times_out_with_a_short_explicit_budget()
    {
        using var project = new TempProject(buildDelaySeconds: 60);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var buildTools = new BuildTools(SolutionManagerTestFactory.Create(), NullLogger<BuildTools>.Instance);

        var report = await buildTools.RunDotNetBuild(
            project.Path,
            timeoutSeconds: 2,
            cancellationToken: deadline.Token);

        Assert.StartsWith("## Build timed out", report, StringComparison.Ordinal);
        Assert.Contains("MCP_DOTNET_TIMEOUT", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_timeout_disabled_remains_cancellable_during_an_active_delay()
    {
        using var project = new TempProject(buildDelaySeconds: 60);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var buildTools = new BuildTools(SolutionManagerTestFactory.Create(), NullLogger<BuildTools>.Instance);
        var build = buildTools.RunDotNetBuild(project.Path, timeoutSeconds: 0, cancellationToken: deadline.Token);
        var marker = System.IO.Path.Combine(project.Root, "started");
        try
        {
            while (!File.Exists(marker) && !build.IsCompleted)
            {
                await Task.Delay(20, deadline.Token);
            }

            Assert.True(File.Exists(marker) && !build.IsCompleted, "Expected the delayed build target to be active.");
        }
        finally
        {
            deadline.Cancel();
        }

        var report = await build.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("Build was cancelled.", report, StringComparison.Ordinal);
        Assert.Contains("MCP_DOTNET_CANCELLED", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_preserves_diagnostic_looking_application_output(bool includeWarnings)
    {
        using var project = new TempProject();
        var tools = new RunTools(NullLogger<RunTools>.Instance);

        var report = await tools.RunDotNetRun(project.Path, includeBuildWarnings: includeWarnings);

        Assert.Contains("dotnet run succeeded", report);
        Assert.Contains("warning CS9999: application stdout", report);
        Assert.Contains("warning CS9998: application stderr", report);
        Assert.Equal(includeWarnings, report.Contains("MSB9001", StringComparison.Ordinal));
        Assert.DoesNotContain("Execution context", report);
        Assert.DoesNotContain("dotnet host", report);
    }

    [Fact]
    public async Task Failed_build_prevents_application_start_and_includes_context()
    {
        using var project = new TempProject();
        File.WriteAllText(System.IO.Path.Combine(project.Root, "Program.cs"), "invalid C# source");

        var report = await new RunTools(NullLogger<RunTools>.Instance).RunDotNetRun(project.Path);

        Assert.Contains("Application was not started", report);
        Assert.Contains("**error**", report);
        Assert.Contains("### Execution context", report);
        Assert.DoesNotContain("MSB9001", report);
        Assert.DoesNotContain("application stdout", report);
    }

    private sealed class TempProject : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcp-build-output-" + Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(Root, "App.csproj");

        public TempProject(int? buildDelaySeconds = null)
        {
            Directory.CreateDirectory(Root);
            var delayTarget = buildDelaySeconds is { } seconds
                ? $"""
                   <Target Name="DelayBuild" BeforeTargets="CoreCompile">
                     <WriteLinesToFile File="$(MSBuildProjectDirectory)/started" Lines="started" Overwrite="true" />
                     {MsBuildDelayFixture.Exec(seconds)}
                   </Target>
                   """
                : string.Empty;
            File.WriteAllText(Path, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                  <Target Name="ReportWarning" BeforeTargets="Build">
                    <Warning Code="MSB9001" Text="build-only-warning" />
                  </Target>
                  {delayTarget}
                </Project>
                """);
            File.WriteAllText(System.IO.Path.Combine(Root, "Program.cs"), """
                System.Console.WriteLine("warning CS9999: application stdout");
                System.Console.Error.WriteLine("warning CS9998: application stderr");
                """);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
