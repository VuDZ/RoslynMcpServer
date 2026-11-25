using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceEntryPolicyTests : IDisposable
{
    private const string CustomGuidance = "Use App.slnx for this repository.";

    public WorkspaceEntryPolicyTests()
    {
        MsBuildBootstrapper.Register();
        _root = Directory.CreateTempSubdirectory("mcp-workspace-entry-");
        _projectPath = Path.Combine(_root.FullName, "App.csproj");
        _sourcePath = Path.Combine(_root.FullName, "Sample.cs");
        _solutionPath = Path.Combine(_root.FullName, "App.slnx");
        File.WriteAllText(_projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(_solutionPath, """<Solution><Project Path="App.csproj" /></Solution>""");
        File.WriteAllText(_sourcePath, "public sealed class Sample { }");
    }

    [Fact]
    public async Task Direct_load_and_build_keep_their_results_and_end_with_the_custom_guidance()
    {
        using var host = CreateHost(new() { ["direct-project-guidance"] = CustomGuidance });
        var manager = host.Services.GetRequiredService<SolutionManager>();
        var build = host.Services.GetRequiredService<BuildTools>();
        var workspace = host.Services.GetRequiredService<WorkspaceTools>();

        var built = await build.RunDotNetBuild(_projectPath);
        Assert.Contains("Build succeeded", built, StringComparison.Ordinal);
        Assert.EndsWith("Warning: " + CustomGuidance, built, StringComparison.Ordinal);

        var loaded = await workspace.LoadWorkspace(_projectPath);
        Assert.NotNull(manager.GetCurrentSolution());
        Assert.EndsWith("Warning: " + CustomGuidance, loaded, StringComparison.Ordinal);

        var cached = await workspace.LoadWorkspace(_projectPath);
        Assert.True(manager.LastLoadWasCacheHit);
        Assert.EndsWith("Warning: " + CustomGuidance, cached, StringComparison.Ordinal);

        var missing = await workspace.LoadWorkspace(Path.Combine(_root.FullName, "Missing.csproj"));
        Assert.Contains("Solution or project file not found.", missing, StringComparison.Ordinal);
        Assert.EndsWith("Warning: " + CustomGuidance, missing, StringComparison.Ordinal);

        await File.WriteAllTextAsync(_sourcePath, "public sealed class Sample : MissingType { }");
        var failed = await build.RunDotNetBuild(_projectPath);
        Assert.Contains("CS0246", failed, StringComparison.Ordinal);
        Assert.EndsWith("Warning: " + CustomGuidance, failed, StringComparison.Ordinal);

        var continuation = await build.RunDotNetBuild(_projectPath, reportCursor: "invalid-cursor");
        Assert.DoesNotContain(CustomGuidance, continuation, StringComparison.Ordinal);
        await manager.ClearWorkspaceAsync();
    }

    [Fact]
    public async Task Strict_mode_refuses_direct_entry_before_build_and_allows_a_solution_project_target()
    {
        using var host = CreateHost(new()
        {
            ["require-solution"] = true,
            ["direct-project-guidance"] = CustomGuidance,
        });
        var manager = host.Services.GetRequiredService<SolutionManager>();
        var workspace = host.Services.GetRequiredService<WorkspaceTools>();
        var build = host.Services.GetRequiredService<BuildTools>();
        var utilities = host.Services.GetRequiredService<UtilityTools>();

        var rejectedLoad = await workspace.LoadWorkspace(_projectPath);
        var rejectedBuild = await build.RunDotNetBuild(_projectPath);
        var rejectedList = await utilities.ListProjects(_projectPath);
        var rejectedGraph = await utilities.GetProjectGraph(_projectPath);
        Assert.All(new[] { rejectedLoad, rejectedBuild, rejectedList, rejectedGraph }, result =>
        {
            Assert.StartsWith("Error: Direct .csproj", result, StringComparison.Ordinal);
            Assert.EndsWith(CustomGuidance, result, StringComparison.Ordinal);
        });
        Assert.False(Directory.Exists(Path.Combine(_root.FullName, "obj")));
        Assert.Null(manager.GetCurrentSolution());
        if (OperatingSystem.IsWindows())
        {
            const string policyError = "Error: Direct .csproj";
            Assert.StartsWith(policyError, await workspace.LoadWorkspace(_projectPath + "."), StringComparison.Ordinal);
            Assert.StartsWith(policyError, await build.RunDotNetBuild(_projectPath + " "), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(_root.FullName, "obj")));
        }

        var builtSolutionProject = await build.RunDotNetBuild(_solutionPath, projectName: "App");
        Assert.True(builtSolutionProject.Contains("Build succeeded", StringComparison.Ordinal), builtSolutionProject);
        Assert.DoesNotContain(CustomGuidance, builtSolutionProject, StringComparison.Ordinal);

        var loadedSolution = await workspace.LoadWorkspace(_solutionPath);
        Assert.Equal(_solutionPath, manager.GetLoadedWorkspacePath());
        Assert.DoesNotContain(CustomGuidance, loadedSolution, StringComparison.Ordinal);
        await manager.ClearWorkspaceAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Lazy_load_returns_guidance_to_the_call_that_triggered_it(bool requireSolution, bool missing)
    {
        var path = missing ? Path.Combine(_root.FullName, "Missing.csproj") : _projectPath;
        using var host = CreateHost(new()
        {
            ["workspace-path"] = path,
            ["require-solution"] = requireSolution,
            ["direct-project-guidance"] = CustomGuidance,
        });
        var manager = host.Services.GetRequiredService<SolutionManager>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream(), "workspace-entry-test");
        await using var server = McpServer.Create(
            transport,
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            host.Services.GetService<ILoggerFactory>(),
            host.Services);
        var listening = server.RunAsync(deadline.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(input.Writer.AsStream(), output.Reader.AsStream()),
                cancellationToken: deadline.Token);
            var arguments = new Dictionary<string, object?> { ["filePath"] = _sourcePath };
            var response = await client.CallToolAsync("get_class_skeleton", arguments, cancellationToken: deadline.Token);
            var blocks = response.Content.OfType<TextContentBlock>().ToArray();
            Assert.Single(blocks, block => block.Text.Contains(CustomGuidance, StringComparison.Ordinal));
            if (requireSolution)
            {
                Assert.Contains("Direct .csproj entry points are disabled", blocks[^1].Text, StringComparison.Ordinal);
                Assert.False(Directory.Exists(Path.Combine(_root.FullName, "obj")));
                Assert.Null(manager.GetCurrentSolution());
                return;
            }

            Assert.Equal("Warning: " + CustomGuidance, blocks[^1].Text);
            if (missing)
            {
                Assert.Contains("The file was not found in the workspace", blocks[0].Text, StringComparison.Ordinal);
                Assert.Contains("Solution or project file not found.", manager.LastLazyLoadFailureReport, StringComparison.Ordinal);
                return;
            }

            Assert.Contains("class Sample", blocks[0].Text, StringComparison.Ordinal);
            var next = await client.CallToolAsync("get_class_skeleton", arguments, cancellationToken: deadline.Token);
            var nextText = Assert.Single(next.Content.OfType<TextContentBlock>()).Text;
            Assert.DoesNotContain(CustomGuidance, nextText, StringComparison.Ordinal);
        }
        finally
        {
            await deadline.CancelAsync();
            try
            {
                await listening;
            }
            catch (OperationCanceledException)
            {
            }

            await manager.ClearWorkspaceAsync();
        }
    }

    public void Dispose()
    {
        _root.Delete(recursive: true);
    }

    private IHost CreateHost(Dictionary<string, object?> values)
    {
        var configPath = Path.Combine(_root.FullName, RoslynMcpFileSettings.FileName);
        File.WriteAllText(configPath, JsonSerializer.Serialize(values));
        var settings = RoslynMcpFileSettings.LoadFromDirectories(executableDirectory: null, workingDirectory: _root.FullName);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(settings);
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var host = builder.Build();
        host.Services.GetRequiredService<SolutionManager>().SearchPrewarmEnabled = false;
        return host;
    }

    private readonly DirectoryInfo _root;
    private readonly string _projectPath;
    private readonly string _sourcePath;
    private readonly string _solutionPath;
}
