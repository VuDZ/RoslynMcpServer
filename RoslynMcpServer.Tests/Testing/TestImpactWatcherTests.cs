using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class TestImpactWatcherTests
{
    [Fact]
    public async Task Native_new_test_file_is_directly_included_before_and_after_reload_without_unrelated_projects()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Files.Root, "Specs", "Fresh.cs");
        const string source = "class Fresh { [Fact] void NewTest() {} }";
        await File.WriteAllTextAsync(path, source);
        await fixture.WaitForEventAsync(path);
        Assert.True(fixture.Manager.ProjectGraphStaleFromComposition);
        Assert.False(fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.Empty(fixture.Solution.GetDocumentIdsWithFilePath(path));
        var before = await fixture.MapAsync(path);
        Assert.Equal(["Specs"], fixture.ProjectNames(before.TestProjectIds));
        Assert.True(before.RequiresReload);
        AssertDirectNewTest(fixture, before, path, source);

        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.NotEmpty(fixture.Solution.GetDocumentIdsWithFilePath(path));
        var after = await fixture.MapAsync(path);
        Assert.Equal(["Specs"], fixture.ProjectNames(after.TestProjectIds));
        Assert.False(after.RequiresReload);
        AssertDirectNewTest(fixture, after, path, source);
    }

    [Fact]
    public async Task Native_unmapped_non_CSharp_file_is_uncovered_with_reload_and_does_not_expand_tests()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Files.Root, "Specs", "notes.txt");
        await File.WriteAllTextAsync(path, "Notes.");
        await fixture.WaitForEventAsync(path);
        var result = await fixture.MapAsync(path);

        Assert.True(fixture.Manager.ProjectGraphStaleFromComposition);
        Assert.False(fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.Empty(result.TestProjectIds);
        Assert.Equal([path], result.NotCoveredPaths);
        Assert.True(result.RequiresReload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_deleted_or_excluded_source_does_not_request_a_second_reload(bool excluded)
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = excluded ? fixture.ExcludedSourcePath : fixture.DeletedSourcePath;
        if (excluded)
        {
            Assert.Empty(fixture.Solution.GetDocumentIdsWithFilePath(path));
            await File.AppendAllTextAsync(path, " // changed");
        }
        else
        {
            File.Delete(path);
        }
        await fixture.WaitForEventAsync(path);
        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.Empty(fixture.Solution.GetDocumentIdsWithFilePath(path));
        Assert.Null(fixture.Manager.DiskWatcherSession!.InputMap!.Find(path));
        var result = await fixture.MapAsync(path);

        Assert.Equal(excluded ? ["Specs"] : ["Library"], fixture.ProjectNames(result.ProjectIds));
        Assert.Equal(["Specs"], fixture.ProjectNames(result.TestProjectIds));
        Assert.False(result.RequiresReload);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Theory]
    [InlineData("into")]
    [InlineData("out")]
    [InlineData("restore")]
    public async Task Native_directory_membership_changes_remain_visible_without_expanding_unrelated_tests(string operation)
    {
        await using var fixture = new TestImpactWatcherFixture();
        var outside = Path.Combine(fixture.Files.Root, "Detached");
        var inside = Path.Combine(fixture.Files.Root, "Specs", "Sub");
        var initial = operation == "into" ? outside : inside;
        Directory.CreateDirectory(initial);
        await File.WriteAllTextAsync(Path.Combine(initial, "More.cs"), "class More { [Fact] void MoreTest() {} }");
        await fixture.LoadAsync();
        if (operation == "into")
        {
            Directory.Move(outside, inside);
            await fixture.WaitForEventAsync(inside);
        }
        else if (operation == "out")
        {
            Directory.Move(inside, outside);
            await fixture.WaitForEventAsync(inside);
        }
        else
        {
            Directory.Delete(inside, recursive: true);
            await fixture.WaitForEventAsync(inside);
            var revision = fixture.Manager.DiskWatcherSession!.EventRevision(inside);
            Directory.CreateDirectory(inside);
            await File.WriteAllTextAsync(Path.Combine(inside, "More.cs"), "class More { [Fact] void MoreTest() {} }");
            await fixture.WaitForEventAsync(inside, revision);
        }
        var result = await fixture.MapAsync(inside);

        Assert.False(fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.True(result.RequiresReload);
        Assert.True(result.NotCoveredPaths.Contains(inside, InputPathCanon.Comparer)
            || fixture.ProjectNames(result.TestProjectIds).Contains("Specs", StringComparer.Ordinal),
            "The directory change must remain uncovered or expand its project.");
        Assert.DoesNotContain("Isolated", fixture.ProjectNames(result.TestProjectIds));
    }

    [Fact]
    public async Task Native_graph_file_change_still_includes_every_test_project()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Files.Root, "Directory.Build.props");
        await File.WriteAllTextAsync(path, "<Project />");
        await fixture.WaitForEventAsync(path);
        Assert.True(fixture.Manager.ProjectGraphStaleFromGraphFile);
        var mapped = await fixture.MapAsync(path);
        var result = TestImpactComposer.Compose(fixture.Solution, mapped, [], [], new HashSet<string>(), CancellationToken.None);

        Assert.Equal(["Isolated", "Specs"], fixture.ProjectNames(result.ExpandedTestProjects.Keys));
        Assert.True(result.RequiresReload);
        Assert.True(result.ProjectGraphStale);
        Assert.All(result.ExpandedTestProjects.Values, Assert.NotEmpty);
    }

    private static void AssertDirectNewTest(TestImpactWatcherFixture fixture, ProjectImpactResult mapped, string path, string source)
    {
        var project = fixture.Solution.Projects.Single(project => project.Name == "Specs");
        var snapshot = BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(source));
        var document = new DocumentImpact(project.Id, path, snapshot, null);
        var result = TestImpactComposer.Compose(fixture.Solution, mapped, [document], [], new HashSet<string>(), CancellationToken.None);
        var test = Assert.Single(result.Tests);
        Assert.Equal("NewTest", test.Key.Name);
        Assert.NotEmpty(test.Reasons);
        Assert.Equal(["Specs"], fixture.ProjectNames(result.ExpandedTestProjects.Keys));
    }
}
