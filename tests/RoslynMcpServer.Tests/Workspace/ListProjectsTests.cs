using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class ListProjectsTests : IAsyncLifetime
{
    private string ProjectPath => Path.Combine(_directory.FullName, "App.csproj");

    public ListProjectsTests()
    {
        MsBuildBootstrapper.Register();
        _directory = Directory.CreateTempSubdirectory("rmcp-list-projects-");
        _manager = SolutionManagerTestFactory.Create(logger: _workspaceLogger);
        _tools = new UtilityTools(NullLogger<UtilityTools>.Instance, _manager);
        File.WriteAllText(ProjectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(Path.Combine(_directory.FullName, "Program.cs"), """
            internal static class Program
            {
                private static void Main()
                {
                }
            }
            """);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _manager.ClearWorkspaceAsync();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task ListProjects_reports_imported_properties_of_the_loaded_configuration()
    {
        WriteProperties("""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType Condition="'$(Configuration)' == 'Release'">Exe</OutputType>
                <OutputType Condition="'$(Configuration)' != 'Release'">Library</OutputType>
              </PropertyGroup>
            </Project>
            """);

        AssertMetadata(await _tools.ListProjects(ProjectPath), "net10.0", "Library");

        await _manager.LoadAsync(ProjectPath, CancellationToken.None, configuration: "Release");
        AssertMetadata(await _tools.ListProjects(), "net10.0", "Exe");
    }

    [Fact]
    public async Task ListProjects_reports_the_selected_instance_of_a_multitarget_project()
    {
        WriteProperties("""
            <Project>
              <PropertyGroup>
                <TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>
                <OutputType Condition="'$(TargetFramework)' == 'net10.0'">Exe</OutputType>
                <OutputType Condition="'$(TargetFramework)' == 'netstandard2.1'">Library</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var solutionPath = Path.Combine(_directory.FullName, "App.slnx");
        File.WriteAllText(solutionPath, """<Solution><Project Path="App.csproj" /></Solution>""");
        var all = await _tools.ListProjects(solutionPath);
        AssertMetadata(all, "netstandard2.1", "Library");
        AssertMetadata(all, "net10.0", "Exe");

        await _manager.LoadAsync(ProjectPath, CancellationToken.None, targetFramework: "netstandard2.1");
        var standard = await _tools.ListProjects();
        AssertMetadata(standard, "netstandard2.1", "Library");
        Assert.DoesNotContain("TFM: net10.0", standard, StringComparison.Ordinal);

        await _manager.LoadAsync(ProjectPath, CancellationToken.None, targetFramework: "net10.0");
        var net = await _tools.ListProjects();
        AssertMetadata(net, "net10.0", "Exe");
        Assert.DoesNotContain("TFM: netstandard2.1", net, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListProjects_keeps_the_explicit_load_when_another_load_is_queued()
    {
        WriteProperties("""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        var otherProject = Path.Combine(_directory.CreateSubdirectory("Other").FullName, "Other.csproj");
        File.WriteAllText(otherProject, """
            <Project>
              <PropertyGroup>
                <TargetFramework>netstandard2.1</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <Target Name="Compile" />
              <Target Name="CoreCompile" />
            </Project>
            """);
        var otherEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOther = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _manager.AfterPhysicalLoadBeforePrepareAsync = async cancellationToken =>
        {
            otherEntered.TrySetResult();
            await releaseOther.Task.WaitAsync(cancellationToken);
        };
        Task? otherLoad = null;
        _workspaceLogger.MessageLogged += message =>
        {
            // Queue the competing load while the explicit load still holds the workspace lock.
            if (message.StartsWith($"Loaded Roslyn workspace from {ProjectPath} (", StringComparison.Ordinal))
            {
                otherLoad = _manager.LoadAndPrepareAsync(
                    otherProject,
                    shadowCopyInSolutionAnalyzers: true,
                    CancellationToken.None);
            }
        };

        var list = _tools.ListProjects(ProjectPath);
        try
        {
            await otherEntered.Task.WaitAsync(TimeSpan.FromMinutes(1));
            var result = await list.WaitAsync(TimeSpan.FromSeconds(10));
            AssertMetadata(result, "net10.0", "Exe");
            Assert.Contains(ProjectPath, result, StringComparison.Ordinal);
            Assert.DoesNotContain(otherProject, result, StringComparison.Ordinal);
        }
        finally
        {
            releaseOther.TrySetResult();
            if (otherLoad is not null)
            {
                await otherLoad;
            }
        }

        AssertMetadata(await _tools.ListProjects(), "netstandard2.1", "Library");
    }

    [Theory]
    [InlineData("<TargetPath>App.dll</TargetPath>", "(unknown)")]
    [InlineData("<TargetPath>App.dll</TargetPath><OutputType>Junk</OutputType>", "Junk")]
    [InlineData("<OutputType>Library</OutputType>", "Library")]
    public async Task ListProjects_reports_evaluated_output_type_independently_of_the_target_path(
        string properties,
        string outputType)
    {
        File.WriteAllText(ProjectPath, $$"""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                {{properties}}
              </PropertyGroup>
              <Target Name="Compile" />
              <Target Name="CoreCompile" />
            </Project>
            """);

        AssertMetadata(await _tools.ListProjects(ProjectPath), "net10.0", outputType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_metadata_keeps_the_framework_and_loader_encoding_when_the_output_type_log_is_unavailable(bool corruptLog)
    {
        WriteProperties("""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);
        if (corruptLog)
        {
            var logPath = Path.Combine(_directory.FullName, ProjectLoadMetadataCapture.BinlogName + ".projects.json");
            File.WriteAllText(logPath, "{incomplete");
        }

        using var workspace = MSBuildWorkspace.Create();
        var capture = new ProjectLoadMetadataCapture();
        await workspace.OpenProjectAsync(ProjectPath, progress: capture);
        var metadata = Assert.Single(capture.Bind(workspace.CurrentSolution, _directory.FullName, CancellationToken.None)).Value;

        Assert.Equal("net10.0", metadata.TargetFramework);
        Assert.Null(metadata.OutputType);
        Assert.Equal(0, metadata.CodePage);
    }

    [Fact]
    public async Task ListProjects_reports_unknown_without_an_evaluation()
    {
        WriteProperties("""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        _manager.FailNextAnalyzerProvenanceCapture = AnalyzerProvenanceCaptureFailureMode.Missing;
        AssertMetadata(await _tools.ListProjects(ProjectPath), "net10.0", "Exe");

        await _manager.ClearWorkspaceAsync();
        Assert.Contains("No workspace loaded.", await _tools.ListProjects(), StringComparison.Ordinal);

        File.WriteAllText(ProjectPath, "<Project />");
        AssertMetadata(await _tools.ListProjects(ProjectPath), "(unknown)", "(unknown)");
    }

    private void WriteProperties(string content) =>
        File.WriteAllText(Path.Combine(_directory.FullName, "Directory.Build.props"), content);

    private static void AssertMetadata(string result, string targetFramework, string outputType) =>
        Assert.Contains(
            $"  TFM: {targetFramework}{Environment.NewLine}  OutputType: {outputType}",
            result,
            StringComparison.Ordinal);

    private readonly DirectoryInfo _directory;
    private readonly WorkspaceRecordingLogger _workspaceLogger = new();
    private readonly SolutionManager _manager;
    private readonly UtilityTools _tools;
}
