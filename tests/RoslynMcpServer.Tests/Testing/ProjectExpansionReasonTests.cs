using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

/// <summary>
/// Epoch 1 keeps why it selected a test project whole as a value, and its run plan shows the same lines as
/// before the value existed.
/// </summary>
public sealed class ProjectExpansionReasonTests
{
    [Fact]
    public async Task CSharp_edit_of_a_dependency_is_a_CSharp_source_reason_with_its_owner()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Library", "Code.cs");
        var change = Change(fixture, fixture.Library, path, "class C { int Run() => 1; }", "class C { int Run() => 2; }");

        var result = Compose(fixture, await fixture.MapAsync(path), [change]);

        var reason = Assert.Single(result.ExpandedTestProjects[fixture.Specs]);
        Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.CSharpSource, fixture.Library, path), reason);
        AssertPlanShows(result, fixture.Specs, "Project expansion without a call graph: " + path);
    }

    [Fact]
    public async Task Additional_file_is_a_non_CSharp_reason_with_its_owner()
    {
        using var fixture = new ProjectImpactFixture();

        var result = Compose(fixture, await fixture.MapAsync(fixture.DataPath), []);

        var reason = Assert.Single(result.ExpandedTestProjects[fixture.Specs]);
        Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.NonCSharpInput, fixture.Library, fixture.DataPath), reason);
        AssertPlanShows(result, fixture.Specs, "Project expansion without a call graph: " + fixture.DataPath);
    }

    [Fact]
    public async Task CSharp_file_listed_as_an_additional_file_is_a_non_CSharp_reason()
    {
        // The role decides, not the extension; estimation parses such a file as an unloaded C# path, and the
        // document impact it adds must not make the reason a C# one.
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Library", "Template.cs");
        fixture.AddAdditional(fixture.Library, path);
        var parsed = new DocumentImpact(
            fixture.Library,
            path,
            BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText("class Template {}")),
            null);

        var result = Compose(fixture, await fixture.MapAsync(path), [parsed]);

        var reason = Assert.Single(result.ExpandedTestProjects[fixture.Specs]);
        Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.NonCSharpInput, fixture.Library, path), reason);
        AssertPlanShows(result, fixture.Specs, "Project expansion without a call graph: " + path);
    }

    [Fact]
    public async Task Test_method_edit_in_a_file_also_used_as_an_additional_file_requires_project_expansion()
    {
        using var fixture = new ProjectImpactFixture();
        fixture.AddAdditional(fixture.Specs, fixture.SpecsPath);
        var change = Change(
            fixture,
            fixture.Specs,
            fixture.SpecsPath,
            "class FactAttribute : System.Attribute {} class Cases { [Fact] void Case() {} }",
            "class FactAttribute : System.Attribute {} class Cases { [Fact] void Case() { int value = 1; } }");

        var result = Compose(fixture, await fixture.MapAsync(fixture.SpecsPath), [change]);

        Assert.Equal("Case", Assert.Single(result.Tests).Key.Name);
        var reason = Assert.Single(result.ExpandedTestProjects.GetValueOrDefault(fixture.Specs, []));
        Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.NonCSharpInput, fixture.Specs, fixture.SpecsPath), reason);
        var plan = Assert.Single(VstestRunPlanner.Plan(result, [], CancellationToken.None));
        Assert.Null(plan.Filter);
        Assert.Contains("Project expansion without a call graph: " + fixture.SpecsPath, plan.Reasons);
    }

    [Fact]
    public async Task Project_file_is_a_project_file_reason_with_its_owner()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Library", "Library.csproj");

        var result = Compose(fixture, await fixture.MapWithStateAsync(true, false, path), []);

        var reason = Assert.Single(result.ExpandedTestProjects[fixture.Specs]);
        Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.ProjectFile, fixture.Library, path), reason);
        AssertPlanShows(result, fixture.Specs, "Project expansion without a call graph: " + path);
    }

    [Fact]
    public async Task Changed_project_graph_file_selects_every_test_project_with_a_graph_reason()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Directory.Build.props");

        var result = Compose(fixture, await fixture.MapWithStateAsync(true, true, path), []);

        // The file is also an evaluation input of every project, so each owner adds a project-file reason.
        Assert.Equal([fixture.Specs, fixture.Isolated], result.ExpandedTestProjects.Keys);
        Assert.All(result.ExpandedTestProjects.Values, reasons =>
        {
            Assert.Equal(new ProjectExpansionReason(ProjectExpansionKind.ProjectGraphFile, null, null), reasons[0]);
            Assert.All(reasons.Skip(1), reason => Assert.Equal(ProjectExpansionKind.ProjectFile, reason.Kind));
        });
        var plan = VstestRunPlanner.Plan(result, [], CancellationToken.None).Single(plan => plan.ProjectId == fixture.Specs);
        Assert.Equal(
            ["Project graph file changed; reload required.", "Project expansion without a call graph: " + path, "Project-level expansion selects the whole project."],
            plan.Reasons);
    }

    [Fact]
    public async Task Observation_gap_adds_a_gap_reason_to_every_test_project()
    {
        using var fixture = new ProjectImpactFixture();
        var baseline = new BaselineSessionSnapshot(fixture.SolutionPath, 1, DateTime.UtcNow, [], [], false)
        {
            HasObservationGap = true,
        };

        var estimate = await TestImpactEstimator.EstimateAsync(
            fixture.Solution,
            fixture.BuildMap(),
            baseline,
            fixture.SolutionPath,
            projectGraphStale: false,
            projectGraphStaleFromGraphFile: false,
            CancellationToken.None);

        var gap = new ProjectExpansionReason(ProjectExpansionKind.ObservationGap, null, null);
        Assert.Contains(gap, estimate.Impact.ExpandedTestProjects[fixture.Specs]);
        Assert.Contains(gap, estimate.Impact.ExpandedTestProjects[fixture.Isolated]);
        Assert.Contains(
            "Observation gap: non-C# inputs may have changed; conservative whole-project expansion.",
            estimate.Plans.Single(plan => plan.ProjectId == fixture.Specs).Reasons);
    }

    [Fact]
    public async Task One_path_with_two_owners_shows_its_line_once()
    {
        // The shared file belongs to Library and Isolated; before the reasons were values, one text was kept.
        using var fixture = new ProjectImpactFixture();
        fixture.AddProjectReference(fixture.Isolated, fixture.Library);
        var library = Change(fixture, fixture.Library, fixture.SharedPath, "public class Shared {}", "public class Shared { int X; }");
        var isolated = Change(fixture, fixture.Isolated, fixture.SharedPath, "public class Shared {}", "public class Shared { int X; }");

        var result = Compose(fixture, await fixture.MapAsync(fixture.SharedPath), [library, isolated]);

        Assert.Equal(2, result.ExpandedTestProjects[fixture.Isolated].Count);
        var plan = VstestRunPlanner.Plan(result, [], CancellationToken.None).Single(plan => plan.ProjectId == fixture.Isolated);
        Assert.Single(plan.Reasons, reason => reason == "Project expansion without a call graph: " + fixture.SharedPath);
    }

    private static void AssertPlanShows(TestImpactResult result, ProjectId project, string line)
    {
        var plan = VstestRunPlanner.Plan(result, [], CancellationToken.None).Single(plan => plan.ProjectId == project);
        Assert.Null(plan.Filter);
        Assert.Equal([line, "Project-level expansion selects the whole project."], plan.Reasons);
    }

    private static DocumentImpact Change(ProjectImpactFixture fixture, ProjectId project, string path, string before, string current)
    {
        var previous = BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(before));
        var snapshot = BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(current));
        fixture.ReplaceSource(project, path, current);
        return new DocumentImpact(project, path, snapshot, BaselineDocumentChangeDetector.Compare(previous, snapshot));
    }

    private static TestImpactResult Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) =>
        TestImpactComposer.Compose(fixture.Solution, mapped, changes, [], new HashSet<string>(), CancellationToken.None);
}
