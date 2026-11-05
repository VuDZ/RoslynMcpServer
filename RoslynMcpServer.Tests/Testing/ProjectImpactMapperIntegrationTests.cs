using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class ProjectImpactMapperIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_build_outputs_are_ignored_but_explicit_obj_input_is_kept(bool solutionInBuild)
    {
        using var fixture = new ProjectImpactFixture(solutionInBuild: solutionInBuild);
        fixture.WriteBuildProjects();
        var build = await DotNetCliRunner.RunAsync(
            "build \"" + fixture.SolutionPath + "\" --nologo -c Release",
            fixture.Root,
            CancellationToken.None,
            TimeSpan.FromMinutes(2));
        Assert.True(build.ExitCode == 0, build.CombinedOutput);
        MsBuildBootstrapper.Register();
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            var solution = await manager.LoadAsync(fixture.SolutionPath);
            var map = manager.DiskWatcherSession!.InputMap!;
            var outputPaths = new[]
            {
                Path.Combine(fixture.AppDirectory, "bin", "Release", "net10.0", "App.deps.json"),
                Path.Combine(fixture.AppDirectory, "obj", "Release", "net10.0", "App.csproj.CoreCompileInputs.cache"),
            };
            Assert.All(outputPaths, path =>
            {
                Assert.True(File.Exists(path), path);
                Assert.Null(map.Find(path));
            });
            var output = await ProjectImpactMapper.MapAsync(solution, map, outputPaths,
                fixture.SolutionPath, manager.ProjectGraphStale, manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);
            Assert.Empty(output.ProjectIds);
            Assert.Empty(output.TestProjectIds);
            Assert.Empty(output.NotCoveredPaths);
            Assert.False(output.RequiresReload);
            Assert.False(output.ProjectGraphStale);

            Assert.NotNull(map.Find(fixture.ObjInputPath));
            var input = await ProjectImpactMapper.MapAsync(solution, map, [fixture.ObjInputPath],
                fixture.SolutionPath, manager.ProjectGraphStale, manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);
            Assert.Equal(["Library"], ProjectNames(solution, input.ProjectIds));
            Assert.Equal(["Specs"], ProjectNames(solution, input.TestProjectIds));
            Assert.Empty(input.NotCoveredPaths);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task New_ProjectReference_uses_all_tests_until_real_reload_then_the_updated_graph()
    {
        using var fixture = new ProjectImpactFixture();
        fixture.WriteBuildProjects();
        MsBuildBootstrapper.Register();
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            var solution = await manager.LoadAsync(fixture.SolutionPath);
            manager.SilenceDiskWatchersForTests();
            var specsPath = solution.Projects.Single(project => project.Name == "Specs").FilePath!;
            var isolated = solution.Projects.Single(project => project.Name == "Isolated");
            var text = await File.ReadAllTextAsync(specsPath);
            var reference = "<ItemGroup><ProjectReference Include=\"../Isolated/Isolated.csproj\" /></ItemGroup>";
            await File.WriteAllTextAsync(specsPath, text.Replace("</Project>", reference + "</Project>", StringComparison.Ordinal));
            var session = manager.DiskWatcherSession!;
            manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(
                WatcherChangeTypes.Changed, Path.GetDirectoryName(specsPath)!, Path.GetFileName(specsPath)));
            Assert.True(manager.ProjectGraphStale);
            Assert.DoesNotContain(solution.GetProjectDependencyGraph()
                .GetProjectsThatTransitivelyDependOnThisProject(isolated.Id), id => solution.GetProject(id)!.Name == "Specs");
            var before = await ProjectImpactMapper.MapAsync(solution, session.InputMap!, [specsPath],
                fixture.SolutionPath, manager.ProjectGraphStale, manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);
            Assert.Equal(["Isolated", "Specs"], ProjectNames(solution, before.TestProjectIds));
            Assert.True(before.ProjectGraphStale);
            Assert.True(before.RequiresReload);
            Assert.Empty(before.NotCoveredPaths);

            var reloaded = await manager.LoadAsync(fixture.SolutionPath);
            Assert.True(manager.LastLoadReopenedGraph);
            Assert.False(manager.ProjectGraphStale);
            var map = manager.DiskWatcherSession!.InputMap!;
            var after = await ProjectImpactMapper.MapAsync(reloaded, map, [specsPath],
                fixture.SolutionPath, manager.ProjectGraphStale, manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);
            Assert.Equal(["Specs"], ProjectNames(reloaded, after.TestProjectIds));
            Assert.False(after.ProjectGraphStale);
            Assert.False(after.RequiresReload);
            var isolatedSource = Path.Combine(fixture.Root, "Isolated", "Cases.cs");
            var dependency = await ProjectImpactMapper.MapAsync(reloaded, map, [isolatedSource],
                fixture.SolutionPath, manager.ProjectGraphStale, manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);
            Assert.Equal(["Isolated", "Specs"], ProjectNames(reloaded, dependency.TestProjectIds));
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private static string[] ProjectNames(Solution solution, IEnumerable<ProjectId> ids) =>
        ids.Select(id => solution.GetProject(id)!.Name).Order(StringComparer.Ordinal).ToArray();
}
