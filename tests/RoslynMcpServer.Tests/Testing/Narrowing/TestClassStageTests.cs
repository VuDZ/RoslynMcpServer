using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using Xunit;
using static RoslynMcpServer.Tests.Testing.Narrowing.NarrowingSources;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>Which test classes each stage gets, from two compiled versions of a solution.</summary>
public sealed class TestClassStageTests
{
    [Fact]
    public async Task Body_edit_selects_by_references_then_by_scanning_then_by_load_and_leaves_an_unrelated_class_out()
    {
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", BodyEditTests, BodyEditTests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: ["Tests.ReferencesA"],
            discovery: ["Tests.ReferencesA", "Tests.ScansWorkers"],
            loads: ["Tests.LoadsByName", "Tests.ReferencesA", "Tests.ScansWorkers"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.A")], project.ChangedTypes);
        Assert.Empty(project.ChangedAssemblies);
        Assert.Null(project.Stage(TestClassStageLevel.Changed).Filter);
        Assert.Equal("FullyQualifiedName~Tests.ReferencesA.", project.Stage(TestClassStageLevel.References).Filter);
        Assert.All(project.Stages, stage => Assert.Null(stage.Whole));
        Assert.All(
            project.Stage(TestClassStageLevel.DiscoveryWithLoads).Classes,
            selection => Assert.Equal(TestClassSelectionReasons.ReachesChangedType, selection.Reasons));
    }

    [Fact]
    public async Task Comment_edit_with_fresh_assemblies_is_code_unchanged_and_not_the_whole_project()
    {
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 1; // Tuned later."))
            .TestProject("Tests", BodyEditTests, BodyEditTests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.CodeUnchanged, project.Outcome);
        Assert.All(project.Stages, stage => Assert.Empty(stage.Classes));
        Assert.Empty(project.ChangedTypes);
        Assert.Empty(project.ChangedAssemblies);
    }

    [Fact]
    public async Task Body_edit_of_a_type_no_test_reaches_names_the_type_and_is_not_the_whole_project()
    {
        var tests = TestClasses(ReferencesA, ScansWorkers, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Value() => 3;", "public int Value() => 30;"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.ChangedTypesWithoutTests, project.Outcome);
        Assert.All(project.Stages, stage => Assert.Empty(stage.Classes));
        Assert.Equal([new IlTypeName("Lib", "Lib.Untested")], project.ChangedTypes);
    }

    [Fact]
    public async Task Removed_interface_selects_the_test_that_scans_for_implementations_at_the_discovery_stage()
    {
        // The current graph no longer has Handler as an IService: only the graph before the edit finds the test.
        var tests = TestClasses(CountsServices, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public sealed class Handler : IService", "public sealed class Handler"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: ["Tests.CountsServices"],
            loads: ["Tests.CountsServices"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.Handler")], project.ChangedTypes);
    }

    [Fact]
    public async Task Removed_framework_attribute_selects_the_test_that_scans_the_assembly_at_the_discovery_stage()
    {
        var tests = TestClasses(ScansParts, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("[Package.Export]", ""))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: ["Tests.ScansParts"],
            loads: ["Tests.ScansParts"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.Part")], project.ChangedTypes);
    }

    [Fact]
    public async Task Const_edit_puts_the_test_class_that_uses_it_in_the_changed_stage()
    {
        // The compiler writes the value into the test: no edge leads from the test to Limits.
        var tests = TestClasses(UsesLimit, ReferencesA);
        using var scenario = new NarrowingScenario();
        scenario
            .Library("Lib", Lib, LibWith("public const int Max = 1;", "public const int Max = 2;"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib")
            .ReferenceAssembly("Lib", scenario.Built)
            .Output("Tests", scenario.Built.AddMinutes(1));

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: ["Tests.UsesLimit"],
            references: ["Tests.UsesLimit"],
            discovery: ["Tests.UsesLimit"],
            loads: ["Tests.UsesLimit"]);
        Assert.Equal(
            TestClassSelectionReasons.ChangedFingerprint,
            Assert.Single(project.Stage(TestClassStageLevel.Changed).Classes).Reasons);
        Assert.Contains(new IlTypeName("Lib", "Lib.Limits"), project.ChangedTypes);
        Assert.Contains(new IlTypeName("Tests", "Tests.UsesLimit"), project.ChangedTypes);
    }

    [Fact]
    public async Task Edited_test_is_in_the_changed_stage_once_by_epoch_1_and_by_fingerprint()
    {
        var before = TestClasses(ReferencesA, ReferencesB);
        var after = before.Replace("new Lib.A().Run()", "new Lib.A().Run() + 1", StringComparison.Ordinal);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", before, after, "Lib")
            .Edit("Lib")
            .Edit("Tests", selectsWhole: false)
            .ChangedTest("Tests", "Tests.ReferencesA", "Runs");

        var project = await scenario.RunAsync();

        var selection = Assert.Single(project.Stage(TestClassStageLevel.Changed).Classes);
        Assert.Equal("Tests.ReferencesA", selection.Class.RuntimeName);
        Assert.Equal(TestClassSelectionReasons.ChangedFingerprint | TestClassSelectionReasons.ChangedTest, selection.Reasons);
        Assert.Equal(["Tests.ReferencesA"], NarrowingAssert.Names(project, TestClassStageLevel.References));
        Assert.Equal(
            TestClassSelectionReasons.ChangedFingerprint | TestClassSelectionReasons.ChangedTest | TestClassSelectionReasons.ReachesChangedType,
            Assert.Single(project.Stage(TestClassStageLevel.References).Classes).Reasons);
    }

    [Fact]
    public async Task Removed_type_selects_the_test_that_depended_on_it_before_the_edit()
    {
        var tests = TestClasses(CountsServices, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public sealed class OldHandler : IService { }", ""))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: ["Tests.CountsServices"],
            loads: ["Tests.CountsServices"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.OldHandler")], project.ChangedTypes);
    }

    [Theory]
    [InlineData("attribute")]
    [InlineData("resource")]
    public async Task Changed_assembly_fingerprint_selects_tests_that_depend_on_any_type_of_the_assembly(string change)
    {
        var tests = TestClasses(ReferencesA, ReferencesB, UsesHelper);
        var after = change == "attribute"
            ? LibWith("namespace Lib", "[assembly: AssemblyMetadata(\"Mode\", \"Fast\")]" + Environment.NewLine + "namespace Lib")
            : Lib;
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, after)
            .Library("Helper", Helper, Helper)
            .TestProject("Tests", tests, tests, "Lib", "Helper")
            .Edit("Lib");
        if (change == "resource")
        {
            scenario.Resources("Lib", [1], [2]);
        }

        var project = await scenario.RunAsync();

        Assert.Equal(["Lib"], project.ChangedAssemblies);
        Assert.Equal(["Tests.ReferencesA", "Tests.ReferencesB"], NarrowingAssert.Names(project, TestClassStageLevel.References));
        Assert.DoesNotContain("Tests.UsesHelper", NarrowingAssert.Names(project, TestClassStageLevel.DiscoveryWithLoads));
        Assert.Contains(new IlTypeName("Lib", "Lib.Untested"), project.ChangedTypes);
    }

    [Fact]
    public async Task Test_class_found_only_before_the_edit_and_gone_from_the_inventory_is_left_out()
    {
        var before = TestClasses(ReferencesA, DeletedTests);
        var after = TestClasses(ReferencesA);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", before, after, "Lib")
            .Edit("Lib")
            .Edit("Tests", selectsWhole: false);

        var project = await scenario.RunAsync();

        Assert.Contains(new IlTypeName("Tests", "Tests.DeletedTests"), project.ChangedTypes);
        NarrowingAssert.Stages(
            project,
            changed: [],
            references: ["Tests.ReferencesA"],
            discovery: ["Tests.ReferencesA"],
            loads: ["Tests.ReferencesA"]);
        Assert.All(project.Stages, stage => Assert.Null(stage.Whole));
    }

    [Fact]
    public async Task Inventory_class_without_a_graph_node_is_in_the_references_stage_and_wider()
    {
        var tests = TestClasses(ReferencesA, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib")
            .Inventory(classes => classes.Append(Ghost(classes)));

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: ["Tests.Ghost", "Tests.ReferencesA"],
            discovery: ["Tests.Ghost", "Tests.ReferencesA"],
            loads: ["Tests.Ghost", "Tests.ReferencesA"]);
        var ghost = project.Stage(TestClassStageLevel.References).Classes.Single(selection => selection.Class.RuntimeName == "Tests.Ghost");
        Assert.Equal(TestClassSelectionReasons.NoGraphNode, ghost.Reasons);
    }

    [Fact]
    public async Task Inventory_class_without_a_graph_node_does_not_hide_that_the_code_did_not_change()
    {
        var tests = TestClasses(ReferencesA);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 1; // Tuned later."))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib")
            .Inventory(classes => classes.Append(Ghost(classes)));

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.CodeUnchanged, project.Outcome);
        Assert.All(project.Stages, stage => Assert.Empty(stage.Classes));
    }

    [Fact]
    public async Task Project_outside_the_epoch_1_answer_is_not_added_even_when_its_test_reaches_the_change()
    {
        var other = TestClassesIn("Other", ReferencesA);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", TestClasses(ReferencesA), TestClasses(ReferencesA), "Lib")
            .TestProject("Other", other, other, "Lib")
            .Edit("Lib")
            .NotSelectedByEpoch1("Other");

        var result = await scenario.RunAllAsync();

        Assert.Equal([scenario.Id("Tests")], result.Projects.Select(project => project.ProjectId));
        Assert.Null(result.Find(scenario.Id("Other")));
        NarrowingAssert.StagesAreNested(result.Projects[0]);
    }

    [Fact]
    public async Task Load_by_name_in_an_assembly_off_the_edit_path_reaches_a_removed_type()
    {
        // Helper does not depend on Lib, yet the load in it brings in every assembly next to the tests.
        var tests = TestClasses(UsesHelperLoader, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public sealed class OldHandler : IService { }", ""))
            .Library("Helper", HelperWithLoader, HelperWithLoader)
            .TestProject("Tests", tests, tests, "Lib", "Helper")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: [],
            loads: ["Tests.UsesHelperLoader"]);
    }

    [Fact]
    public async Task Added_implementation_selects_the_test_that_scans_for_implementations_by_the_current_graph()
    {
        var tests = TestClasses(CountsServices, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library(
                "Lib",
                Lib,
                LibWith("public sealed class OldHandler : IService { }", "public sealed class OldHandler : IService { }" + Environment.NewLine + "public sealed class NewHandler : IService { }"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: ["Tests.CountsServices"],
            loads: ["Tests.CountsServices"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.NewHandler")], project.ChangedTypes);
    }

    [Fact]
    public async Task Type_that_gains_an_interface_selects_the_test_that_scans_for_it_by_the_current_graph()
    {
        // Only the current graph has the edge from Untested to IService.
        var tests = TestClasses(CountsServices, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public class Untested", "public class Untested : IService"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        NarrowingAssert.Stages(
            project,
            changed: [],
            references: [],
            discovery: ["Tests.CountsServices"],
            loads: ["Tests.CountsServices"]);
        Assert.Equal([new IlTypeName("Lib", "Lib.Untested")], project.ChangedTypes);
    }

    [Fact]
    public async Task Added_test_class_is_in_the_changed_stage_by_its_fingerprint()
    {
        var before = TestClasses(ReferencesA);
        var after = TestClasses(ReferencesA, AddedTests);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", before, after, "Lib")
            .Edit("Lib")
            .Edit("Tests", selectsWhole: false);

        var project = await scenario.RunAsync();

        var selection = Assert.Single(project.Stage(TestClassStageLevel.Changed).Classes);
        Assert.Equal("Tests.AddedTests", selection.Class.RuntimeName);
        Assert.Equal(TestClassSelectionReasons.ChangedFingerprint, selection.Reasons);
        Assert.Equal(["Tests.AddedTests", "Tests.ReferencesA"], NarrowingAssert.Names(project, TestClassStageLevel.References));
    }

    [Fact]
    public async Task Test_class_found_only_before_the_edit_and_missing_from_the_inventory_runs_its_stage_whole()
    {
        // CountsServices is still a test class of the current graph; the inventory cannot name it.
        var tests = TestClasses(CountsServices, ReferencesB);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public sealed class Handler : IService", "public sealed class Handler"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib")
            .Inventory(classes => classes.Where(testClass => testClass.RuntimeName != "Tests.CountsServices"));

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.Narrowed, project.Outcome);
        Assert.Null(project.Stage(TestClassStageLevel.Changed).Whole);
        Assert.Null(project.Stage(TestClassStageLevel.References).Whole);
        var stage = project.Stage(TestClassStageLevel.Discovery);
        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, stage.Whole?.Kind);
        Assert.Equal(new IlTypeName("Tests", "Tests.CountsServices").ToString(), stage.Whole?.Subject);
    }

    /// <summary>A test class of the inventory whose type the graph does not have.</summary>
    private static VstestTestClass Ghost(IReadOnlyList<VstestTestClass> classes)
    {
        var template = classes.Single(testClass => testClass.RuntimeName == "Tests.ReferencesA");
        return template with { RuntimeName = "Tests.Ghost", TopLevelTypeName = "Tests.Ghost" };
    }

    private static readonly string BodyEditTests = TestClasses(ReferencesA, ScansWorkers, LoadsByName, ReferencesB);
}
