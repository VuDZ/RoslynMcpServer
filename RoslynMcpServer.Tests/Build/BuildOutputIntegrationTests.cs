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

        Assert.Equal("## Build succeeded", await buildTools.RunDotNetBuild(project.Path, noIncremental: false));
        var detailed = await buildTools.RunDotNetBuild(project.Path, noIncremental: false, includeBuildWarnings: true);
        Assert.Contains("MSB9001", detailed);
        Assert.DoesNotContain("Execution context", detailed);
        Assert.Equal("## Build succeeded", await utilityTools.ExecuteDotNetCommand($"build \"{project.Path}\""));
        var rawDetailed = await utilityTools.ExecuteDotNetCommand($"build \"{project.Path}\"", includeBuildWarnings: true);
        Assert.Contains("MSB9001", rawDetailed);
        Assert.DoesNotContain("Execution context", rawDetailed);
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

        public TempProject()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                  <Target Name="ReportWarning" BeforeTargets="Build">
                    <Warning Code="MSB9001" Text="build-only-warning" />
                  </Target>
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
