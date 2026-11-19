using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class TestImpactComposerTests
{
    [Theory]
    [InlineData("class Cases { [Fact] void A() {} }", "class Cases { [Fact] void A() { int x = 1; } }", "A", false)]
    [InlineData("class Cases { [Fact] void A() {} }", "class Cases { [Fact] void A() {} [Fact] void B() {} }", "B", false)]
    [InlineData("class Cases { [Fact] void Old() {} }", "class Cases { [Fact] void Renamed() {} }", "Renamed", true)]
    [InlineData("class Cases { void A() {} }", "class Cases { [Fact] void A() {} }", "A", true)]
    [InlineData("class Cases { [Fact] void A() {} [Fact] void B() {} }", "class Cases { [Fact] void B() {} }", "", true)]
    [InlineData("class Cases { Cases() {} [Fact] void A() {} [Fact] void B() {} } class Other { [Fact] void C() {} }",
        "class Cases { Cases() { int x = 1; } [Fact] void A() {} [Fact] void B() {} } class Other { [Fact] void C() {} }", "A,B", false)]
    [InlineData("class Cases { [SetUp] void Start() {} [Test] void A() {} } class Other { [Test] void B() {} }",
        "class Cases { [SetUp] void Start() { int x = 1; } [Test] void A() {} } class Other { [Test] void B() {} }", "A", false)]
    [InlineData("class Cases { int value = 1; [Fact] void A() {} }", "class Cases { int value = 2; [Fact] void A() {} }", "A", false)]
    [InlineData("class Cases { [Fact] void A() {} void Helper() {} }", "class Cases { [Fact] void A() {} void Helper() { int x = 1; } }", "", true)]
    [InlineData("class Fixture {} class Cases { [Fact] void A() {} }", "class Fixture { int Value; } class Cases { [Fact] void A() {} }", "", true)]
    [InlineData("class Cases { [Fact] void A() {} }", "class Cases { [Fact] void A() {} } class NewCases { [Fact] void B() {} }", "B", false)]
    [InlineData("class Cases { [Fact] void A() {} }", "class Cases { [Fact] void A() {} } partial class NewCases { [Fact] void B() {} }", "B", true)]
    [InlineData("class Cases { Cases() {} [Fact] void A() {} class Nested {} }",
        "class Cases { Cases() { int x = 1; } [Fact] void A() {} class Nested {} }", "", true)]
    [InlineData("class Cases { void Helper() {} }", "class Cases { Cases() {} void Helper() {} [Fact] void A() {} }", "A", true)]
    [InlineData("abstract class Cases { [Fact] void A() {} }", "abstract class Cases { [Fact] void A() { int x = 1; } }", "A", true)]
    [InlineData("class Cases { [Fact] void A() {} class Nested {} }", "class Cases { [Fact] void A() { int x = 1; } class Nested {} }", "A", true)]
    [InlineData("class Cases { public static int value = 1; [Fact] void A() {} }", "class Cases { public static int value = 2; [Fact] void A() {} }", "", true)]
    [InlineData("class Cases { static Cases() {} [Fact] void A() {} }", "class Cases { static Cases() { int x = 1; } [Fact] void A() {} }", "", true)]
    public async Task Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(
        string before, string current, string expectedNames, bool expands)
    {
        using var fixture = new ProjectImpactFixture();
        var document = Change(fixture, fixture.Specs, fixture.SpecsPath, before, current);
        var mapped = await fixture.MapAsync(fixture.SpecsPath);
        var result = Compose(fixture, mapped, [document]);

        Assert.Equal(SplitNames(expectedNames), result.Tests.Select(test => test.Key.Name).Order(StringComparer.Ordinal));
        Assert.All(result.Tests, test =>
        {
            Assert.Equal(fixture.Specs, test.ProjectId);
            Assert.NotEmpty(test.Reasons);
        });
        Assert.Equal(expands ? [fixture.Specs] : Array.Empty<ProjectId>(), result.ExpandedTestProjects.Keys);
        Assert.All(result.ExpandedTestProjects.Values, Assert.NotEmpty);
        Assert.Empty(result.NotCoveredPaths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_modification_or_removal_expands_transitively_and_keeps_other_test_changes(bool remove)
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Library", "Code.cs");
        var production = Change(fixture, fixture.Library, path, "class C { int Run() => 1; }",
            remove ? "class C {}" : "class C { int Run() => 2; }");
        var ownTest = Change(fixture, fixture.Specs, fixture.SpecsPath,
            "class Cases { [Fact] void A() {} [Fact] void B() {} }",
            "class Cases { [Fact] void A() { int x = 1; } [Fact] void B() {} }");
        var result = Compose(fixture, await fixture.MapAsync(path, fixture.SpecsPath), [production, ownTest]);

        Assert.Equal([fixture.Specs], result.ExpandedTestProjects.Keys);
        Assert.Equal("A", Assert.Single(result.Tests).Key.Name);
        Assert.NotEmpty(result.ExpandedTestProjects[fixture.Specs]);
    }

    [Theory]
    [InlineData("class Derived : Cases {}", false)]
    [InlineData("class Derived : Cases<int> {}", false)]
    [InlineData("class Derived : N.Cases {}", false)]
    [InlineData("class Derived : N.Cases<int> {}", true)]
    public async Task Other_document_base_names_block_narrowing_for_a_test_or_root(string derivedSource, bool root)
    {
        using var fixture = new ProjectImpactFixture();
        var before = root
            ? "class Cases { Cases() {} [Fact] void A() {} }"
            : "class Cases { [Fact] void A() {} }";
        var current = root
            ? "class Cases { Cases() { int x = 1; } [Fact] void A() {} }"
            : "class Cases { [Fact] void A() { int x = 1; } }";
        var change = Change(fixture, fixture.Specs, fixture.SpecsPath, before, current);
        var baseNames = Snapshot(derivedSource).BaseTypeNames;
        var result = TestImpactComposer.Compose(fixture.Solution, await fixture.MapAsync(fixture.SpecsPath),
            [change], [], baseNames, CancellationToken.None);

        Assert.Contains("Cases", baseNames);
        Assert.Equal([fixture.Specs], result.ExpandedTestProjects.Keys);
        Assert.Equal("A", Assert.Single(result.Tests).Key.Name);
    }

    [Fact]
    public async Task A_root_in_one_partial_document_includes_tests_from_unchanged_partial_documents()
    {
        using var fixture = new ProjectImpactFixture();
        var path = Path.Combine(fixture.Root, "Specs", "Roots.cs");
        var root = Change(fixture, fixture.Specs, path,
            "[TestFixture] partial class Cases { Cases() {} }",
            "[TestFixture] partial class Cases { Cases() { int x = 1; } }");
        var tests = Change(fixture, fixture.Specs, fixture.SpecsPath,
            "partial class Cases { [Fact] void A() {} } class Other { [Fact] void B() {} }",
            "partial class Cases { [Fact] void A() {} } class Other { [Fact] void B() {} }");
        var result = Compose(fixture, await fixture.MapAsync(path), [root, tests]);

        Assert.Equal("A", Assert.Single(result.Tests).Key.Name);
        Assert.Empty(result.ExpandedTestProjects);
    }

    [Fact]
    public async Task Graph_tests_are_unioned_and_deduplicated_after_direct_tests()
    {
        using var fixture = new ProjectImpactFixture();
        var change = Change(fixture, fixture.Specs, fixture.SpecsPath, "class Cases { [Fact] void A() {} }",
            "class Cases { [Fact] void A() { int x = 1; } }");
        var key = change.Current!.Members.Single(member => member.IsTestMethod).Key;
        var graph = new AffectedTest(fixture.Specs, key, ["Graph reachability."]);
        var result = TestImpactComposer.Compose(fixture.Solution, await fixture.MapAsync(fixture.SpecsPath),
            [change], [graph], new HashSet<string>(), CancellationToken.None);

        var test = Assert.Single(result.Tests);
        Assert.Equal(key, test.Key);
        Assert.Contains("Graph reachability.", test.Reasons);
        Assert.True(test.Reasons.Count >= 2);
        Assert.Empty(result.ExpandedTestProjects);
    }

    [Fact]
    public async Task A_non_CSharp_input_in_the_same_project_cannot_be_suppressed_by_a_safe_test_edit()
    {
        using var fixture = new ProjectImpactFixture();
        var data = Path.Combine(fixture.Root, "Specs", "data.json");
        fixture.AddAdditional(fixture.Specs, data);
        var change = Change(fixture, fixture.Specs, fixture.SpecsPath, "class Cases { [Fact] void A() {} }",
            "class Cases { [Fact] void A() { int x = 1; } }");
        var result = Compose(fixture, await fixture.MapAsync(data, fixture.SpecsPath), [change]);

        Assert.Equal([fixture.Specs], result.ExpandedTestProjects.Keys);
        Assert.Single(result.Tests);
    }

    [Fact]
    public async Task Unknown_and_deleted_documents_expand_and_preserve_uncovered_paths()
    {
        using var fixture = new ProjectImpactFixture();
        var missing = Path.Combine(fixture.Root, "unknown.json");
        var mapped = await fixture.MapAsync(fixture.SpecsPath, missing);
        var document = new DocumentImpact(fixture.Specs, fixture.SpecsPath, null, null);
        var result = Compose(fixture, mapped, [document]);

        Assert.Empty(result.Tests);
        Assert.Equal([fixture.Specs], result.ExpandedTestProjects.Keys);
        Assert.Equal([missing], result.NotCoveredPaths);
        Assert.Equal(mapped.RequiresReload, result.RequiresReload);
        Assert.Equal(mapped.ProjectGraphStale, result.ProjectGraphStale);
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        using var fixture = new ProjectImpactFixture();
        var mapped = await fixture.MapAsync(fixture.SpecsPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => TestImpactComposer.Compose(
            fixture.Solution, mapped, [], [], new HashSet<string>(), cancellation.Token));
    }

    private static DocumentImpact Change(ProjectImpactFixture fixture, ProjectId project, string path, string before, string current)
    {
        const string attributes = "class FactAttribute : System.Attribute {} class TestAttribute : System.Attribute {} class SetUpAttribute : System.Attribute {} ";
        var previous = Snapshot(attributes + before);
        var snapshot = Snapshot(attributes + current);
        fixture.ReplaceSource(project, path, attributes + current);
        return new DocumentImpact(project, path, snapshot, BaselineDocumentChangeDetector.Compare(previous, snapshot));
    }

    private static TestImpactResult Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) =>
        TestImpactComposer.Compose(fixture.Solution, mapped, changes, [], new HashSet<string>(), CancellationToken.None);

    private static BaselineDocumentSnapshot Snapshot(string source) =>
        BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(source));

    private static string[] SplitNames(string names) => names.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
