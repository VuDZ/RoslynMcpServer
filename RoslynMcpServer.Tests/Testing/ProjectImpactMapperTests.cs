using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class ProjectImpactMapperTests
{
    [Fact]
    public async Task Non_CSharp_input_expands_through_an_intermediate_project()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(fixture.DataPath);
        AssertIds([fixture.Library], result.ProjectIds);
        AssertIds([fixture.Specs], result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
        Assert.False(result.RequiresReload);
        Assert.False(result.ProjectGraphStale);
    }

    [Fact]
    public async Task Unknown_path_is_reported_once_with_its_file_name()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "unknown.json");
        var result = await fixture.MapAsync(path, path);
        Assert.Empty(result.ProjectIds);
        Assert.Empty(result.TestProjectIds);
        Assert.Equal([path], result.NotCoveredPaths);
        Assert.False(result.RequiresReload);
    }

    [Fact]
    public async Task Linked_CSharp_document_keeps_all_owners_and_their_test_closures()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(fixture.SharedPath);
        AssertIds([fixture.Library, fixture.Isolated], result.ProjectIds);
        AssertIds([fixture.Specs, fixture.Isolated], result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Fact]
    public async Task A_test_project_includes_itself_and_overlapping_paths_are_deduplicated()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(fixture.DataPath, fixture.SpecsPath, fixture.DataPath);
        AssertIds([fixture.Library, fixture.Specs], result.ProjectIds);
        AssertIds([fixture.Specs], result.TestProjectIds);
    }

    [Fact]
    public async Task New_CSharp_file_uses_the_nearest_project_and_requires_reload()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.AppDirectory, "sub", "Fresh.cs");
        var result = await fixture.MapWithStateAsync(true, false, path);
        AssertIds([fixture.App], result.ProjectIds);
        AssertIds([fixture.Specs], result.TestProjectIds);
        Assert.True(result.RequiresReload);
        Assert.True(result.ProjectGraphStale);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Fact]
    public async Task New_CSharp_file_without_a_containing_project_is_not_covered()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "outside", "Fresh.cs");
        var result = await fixture.MapWithStateAsync(true, false, path);
        Assert.Empty(result.ProjectIds);
        Assert.Empty(result.TestProjectIds);
        Assert.Equal([path], result.NotCoveredPaths);
        Assert.True(result.RequiresReload);
    }

    [Theory]
    [InlineData("Library.csproj")]
    [InlineData("Repo.sln")]
    [InlineData("Repo.slnx")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    public async Task Stale_graph_file_includes_all_loaded_test_projects(string name)
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapWithStateAsync(true, Path.Combine(fixture.Root, name));
        AssertIds([fixture.Specs, fixture.Isolated], result.TestProjectIds);
        Assert.True(result.ProjectGraphStale);
        Assert.True(result.RequiresReload);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Fact]
    public async Task Existing_stale_graph_state_also_broadens_an_ordinary_source_change()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapWithStateAsync(true, fixture.SpecsPath);
        AssertIds([fixture.Specs, fixture.Isolated], result.TestProjectIds);
        Assert.True(result.ProjectGraphStale);
        Assert.True(result.RequiresReload);
    }

    [Fact]
    public async Task Ancestor_build_props_maps_to_every_loaded_project_after_reload()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(Path.Combine(fixture.Root, "Directory.Build.props"));
        AssertIds([fixture.Library, fixture.App, fixture.Specs, fixture.Isolated], result.ProjectIds);
        AssertIds([fixture.Specs, fixture.Isolated], result.TestProjectIds);
        Assert.False(result.ProjectGraphStale);
        Assert.False(result.RequiresReload);
    }

    [Theory]
    [InlineData("bin", "App.dll")]
    [InlineData("obj", "Generated.cs")]
    [InlineData("artifacts", "result.json")]
    [InlineData("node_modules", "package.json")]
    [InlineData("TestResults", "test.trx")]
    [InlineData(".git", "config")]
    public async Task Unmapped_ignored_output_is_neither_expanded_nor_uncovered(string directory, string name)
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(Path.Combine(fixture.AppDirectory, directory, name));
        Assert.Empty(result.ProjectIds);
        Assert.Empty(result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
        Assert.False(result.RequiresReload);
    }

    [Fact]
    public async Task An_explicit_input_inside_obj_takes_priority_over_the_output_filter()
    {
        using var fixture = new ProjectImpactFixture();
        var result = await fixture.MapAsync(fixture.ObjInputPath);
        AssertIds([fixture.Library], result.ProjectIds);
        AssertIds([fixture.Specs], result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Fact]
    public async Task Repository_inside_artifacts_does_not_hide_unknown_inputs()
    {
        using var fixture = new ProjectImpactFixture(insideArtifacts: true);
        var path = Path.Combine(fixture.Root, "unknown.json");
        var result = await fixture.MapAsync(path);
        Assert.Empty(result.TestProjectIds);
        Assert.Equal([path], result.NotCoveredPaths);
    }

    [Fact]
    public async Task Solution_in_build_does_not_exempt_a_sibling_projects_output_directory()
    {
        using var fixture = new ProjectImpactFixture(solutionInBuild: true);
        var result = await fixture.MapAsync(Path.Combine(fixture.AppDirectory, "bin", "App.dll"));
        Assert.Empty(result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Theory]
    [InlineData("project.assets.json")]
    [InlineData("packages.lock.json")]
    [InlineData("nuget.config")]
    public async Task Restore_inputs_remain_a_blind_spot_even_when_mapped(string name)
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, name);
        fixture.AddAdditional(fixture.Library, path);
        var result = await fixture.MapAsync(path);
        Assert.Empty(result.ProjectIds);
        Assert.Empty(result.TestProjectIds);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Theory]
    [InlineData("class Cases { [Fact] void Case() {} }", true)]
    [InlineData("class Cases { [TestCase(1)] void Case(int value) {} }", true)]
    [InlineData("class Cases { [TestMethod] void Case() {} }", true)]
    [InlineData("class FactAttribute : System.Attribute {} class CustomAttribute : FactAttribute {} class Cases { [Custom] void Case() {} }", true)]
    [InlineData("using Alias = FactAttribute; class FactAttribute : System.Attribute {} class Cases { [Alias] void Case() {} }", true)]
    [InlineData("class Cases { [DataRow(1)] void Case(int value) {} }", false)]
    public async Task Test_project_recognition_uses_existing_discovery_rules(string source, bool containsTests)
    {
        using var fixture = new ProjectImpactFixture();
        fixture.ReplaceSpecs(source);
        var result = await fixture.MapAsync(fixture.SpecsPath);
        AssertIds(containsTests ? [fixture.Specs] : [], result.TestProjectIds);
    }

    [Fact]
    public async Task Cancelled_mapping_observes_cancellation()
    {
        using var fixture = new ProjectImpactFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectImpactMapper.MapAsync(
            fixture.Solution, fixture.BuildMap(), [fixture.DataPath], fixture.SolutionPath, false, false, cancellation.Token));
    }

    private static void AssertIds(IEnumerable<ProjectId> expected, IReadOnlyList<ProjectId> actual)
    {
        Assert.Equal(expected.OrderBy(id => id.Id), actual.OrderBy(id => id.Id));
    }
}
