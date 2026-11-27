using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class SolutionManagerPassedLoadArgsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "RoslynMcpPassedLoad-" + Guid.NewGuid().ToString("N"));

    private readonly string _csprojPath;

    public SolutionManagerPassedLoadArgsTests()
    {
        MsBuildBootstrapper.Register();
        Directory.CreateDirectory(_root);
        _csprojPath = Path.Combine(_root, "App.csproj");
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Class1.cs"), "namespace App; public sealed class Class1 { }");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public async Task Server_info_reports_working_set_and_counts_successful_physical_loads_across_reset()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "lite" });
        using var host = builder.Build();
        var manager = host.Services.GetRequiredService<SolutionManager>();
        manager.SearchPrewarmEnabled = false;
        var tools = host.Services.GetRequiredService<ServerLifecycleTools>();

        try
        {
            var initial = await tools.GetMcpServerInfo();
            AssertLoadCount(initial, 0);
            Assert.Matches(@"(?m)^- \*\*Process working set:\*\* [0-9]+\.[0-9] MiB\r?$", initial);
            Assert.Null(manager.GetCurrentSolution());

            await manager.LoadAsync(_csprojPath);
            AssertLoadCount(await tools.GetMcpServerInfo(), 1);

            await manager.LoadAsync(_csprojPath);
            Assert.True(manager.LastLoadWasCacheHit);
            AssertLoadCount(await tools.GetMcpServerInfo(), 1);

            await Assert.ThrowsAsync<FileNotFoundException>(() => manager.LoadAsync(Path.Combine(_root, "Missing.csproj")));
            AssertLoadCount(await tools.GetMcpServerInfo(), 1);

            await manager.ClearWorkspaceAsync();
            AssertLoadCount(await tools.GetMcpServerInfo(), 1);
            Assert.Null(manager.GetCurrentSolution());

            await manager.LoadAsync(_csprojPath);
            AssertLoadCount(await tools.GetMcpServerInfo(), 2);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }

        static void AssertLoadCount(string info, int count) => Assert.Contains(
            $"- **Successful physical workspace loads (since server start):** {count}{Environment.NewLine}",
            info,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAndPrepare_omitted_platform_does_not_reopen_or_clear_x64()
    {
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(
                _csprojPath,
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None,
                configuration: "Debug",
                platform: "x64")
            ;

        Assert.Equal("x64", manager.LoadedPlatform);
        Assert.False(manager.LastLoadWasCacheHit);

        await manager.LoadAndPrepareAsync(
                _csprojPath,
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None)
            ;

        Assert.True(manager.LastLoadWasCacheHit);
        Assert.False(manager.LastLoadReopenedGraph);
        Assert.Equal("x64", manager.LoadedPlatform);
        Assert.Equal("Debug", manager.LoadedConfiguration);
    }

    [Fact]
    public async Task LoadAndPrepare_different_configuration_reopens_and_keeps_platform()
    {
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(
                _csprojPath,
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None,
                configuration: "Debug",
                platform: "x64",
                targetFramework: "net10.0")
            ;

        await manager.LoadAndPrepareAsync(
                _csprojPath,
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None,
                configuration: "Release")
            ;

        Assert.True(manager.LastLoadReopenedGraph);
        Assert.False(manager.LastLoadWasCacheHit);
        Assert.Equal("Release", manager.LoadedConfiguration);
        Assert.Equal("x64", manager.LoadedPlatform);
        Assert.Equal("net10.0", manager.LoadedTargetFramework);
    }

    [Fact]
    public async Task EnsureWorkspaceFromConfig_no_file_does_not_load()
    {
        var manager = SolutionManagerTestFactory.Create();
        await manager.EnsureWorkspaceFromConfigAsync();
        Assert.Null(manager.GetCurrentSolution());
        Assert.Equal(WorkspaceLoadSource.None, manager.WorkspaceLoadSource);
    }

    [Fact]
    public async Task EnsureWorkspaceFromConfig_loads_from_file_settings()
    {
        var cwd = Path.Combine(_root, "cfg-cwd");
        Directory.CreateDirectory(cwd);
        var jsonPath = _csprojPath.Replace('\\', '/');
        File.WriteAllText(
            Path.Combine(cwd, RoslynMcpFileSettings.FileName),
            $$"""
            {
              "workspace-path": "{{jsonPath}}",
              "configuration": "Debug",
              "platform": "x64"
            }
            """);
        var settings = RoslynMcpFileSettings.LoadFromDirectories(executableDirectory: null, workingDirectory: cwd);

        var manager = new SolutionManager(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SolutionManager>.Instance,
            new AnalyzerProvenanceCaptureService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerProvenanceCaptureService>.Instance,
                Microsoft.Extensions.Options.Options.Create(new AnalyzerProvenanceCaptureOptions()),
                TimeProvider.System),
            settings);

        var solution = await manager.GetPublishedSolutionAsync();
        Assert.NotNull(solution);
        Assert.Equal(WorkspaceLoadSource.ConfigFile, manager.WorkspaceLoadSource);
        Assert.Equal("Debug", manager.LoadedConfiguration);
        Assert.Equal("x64", manager.LoadedPlatform);
    }

    [Fact]
    public async Task EnsureWorkspaceFromConfig_skips_lazy_load_when_any_jsonc_parse_fails()
    {
        var exeDir = Path.Combine(_root, "cfg-exe-broken");
        var cwdDir = Path.Combine(_root, "cfg-cwd-valid");
        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(cwdDir);
        var jsonPath = _csprojPath.Replace('\\', '/');

        File.WriteAllText(
            Path.Combine(exeDir, RoslynMcpFileSettings.FileName),
            "{ this is not json");
        File.WriteAllText(
            Path.Combine(cwdDir, RoslynMcpFileSettings.FileName),
            $$"""
            {
              "workspace-path": "{{jsonPath}}",
              "configuration": "Debug"
            }
            """);

        var settings = RoslynMcpFileSettings.LoadFromDirectories(exeDir, cwdDir);
        Assert.NotEmpty(settings.ParseFailures);
        Assert.False(string.IsNullOrWhiteSpace(settings.WorkspacePath));

        var manager = new SolutionManager(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SolutionManager>.Instance,
            new AnalyzerProvenanceCaptureService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerProvenanceCaptureService>.Instance,
                Microsoft.Extensions.Options.Options.Create(new AnalyzerProvenanceCaptureOptions()),
                TimeProvider.System),
            settings);

        await manager.EnsureWorkspaceFromConfigAsync();
        Assert.Null(manager.GetCurrentSolution());
        Assert.Equal(WorkspaceLoadSource.None, manager.WorkspaceLoadSource);

        // Explicit load_workspace path still works despite the broken sibling file.
        var load = await manager.LoadAndPrepareAsync(
            _csprojPath,
            shadowCopyInSolutionAnalyzers: false,
            CancellationToken.None);
        Assert.NotNull(load.Solution);
        Assert.Equal(WorkspaceLoadSource.ExplicitLoad, manager.WorkspaceLoadSource);
    }

    [Fact]
    public async Task Concurrent_load_waits_and_does_not_cancel_in_progress_load()
    {
        var manager = SolutionManagerTestFactory.Create();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTokenSawCancel = false;

        manager.AfterPhysicalLoadBeforePrepareAsync = async ct =>
        {
            firstEntered.TrySetResult();
            try
            {
                await releaseFirst.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                firstTokenSawCancel = true;
                throw;
            }
        };

        var firstCts = new CancellationTokenSource();
        var firstTask = manager.LoadAndPrepareAsync(
            _csprojPath,
            shadowCopyInSolutionAnalyzers: true,
            firstCts.Token);

        await firstEntered.Task.WaitAsync(TimeSpan.FromMinutes(2));

        var secondCts = new CancellationTokenSource();
        var secondTask = manager.LoadAndPrepareAsync(
            _csprojPath,
            shadowCopyInSolutionAnalyzers: false,
            secondCts.Token);

        // Give the second caller time to block on the workspace lock.
        await Task.Delay(200);
        secondCts.Cancel();

        releaseFirst.TrySetResult();

        var first = await firstTask;
        Assert.NotNull(first.Solution);
        Assert.False(firstTokenSawCancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await secondTask);
    }
}
