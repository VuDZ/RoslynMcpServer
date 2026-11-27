using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using Xunit;
using static RoslynMcpServer.Tests.Testing.Narrowing.NarrowingSources;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>Every case in which a project selected whole by epoch 1 stays whole, each with its reason.</summary>
public sealed class TestNarrowingWholeTests
{
    [Theory]
    [InlineData("Lib")]
    [InlineData("Tests")]
    public async Task Assembly_on_the_path_missing_at_load_has_no_snapshot_before_the_edit(string project)
    {
        using var scenario = BodyEdit().Before(project, AssemblySnapshotEntry.Missing);

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.NoBeforeSnapshot);

        Assert.Equal(scenario.OutputPath(project), whole.Subject);
    }

    [Fact]
    public async Task Assembly_of_a_project_that_appeared_after_load_has_no_snapshot_before_the_edit()
    {
        using var scenario = BodyEdit().Before("Lib", _ => null);

        NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.NoBeforeSnapshot);
    }

    [Theory]
    [InlineData("Lib")]
    [InlineData("Tests")]
    public async Task Assembly_on_the_path_that_is_unknown_by_the_race_rule_keeps_the_project_whole(string project)
    {
        using var scenario = BodyEdit().Before(project, path => AssemblySnapshotEntry.Unknown(path, "Rebuilt before its first read."));

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.BeforeSnapshotUnknown);

        Assert.Equal(scenario.OutputPath(project), whole.Subject);
    }

    [Fact]
    public async Task Unknown_assembly_off_the_edit_path_with_a_load_by_name_keeps_the_project_whole()
    {
        // Review probe: the removed type is found only through the load in Helper, which the graph before the
        // edit lacks when Helper is unknown.
        using var scenario = LoadOffThePath()
            .Before("Helper", path => AssemblySnapshotEntry.Unknown(path, "Rebuilt before its first read."));

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.BeforeSnapshotUnknown);

        Assert.Equal(scenario.OutputPath("Helper"), whole.Subject);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unread_method_bodies_off_the_edit_path_keep_the_project_whole(bool before)
    {
        using var scenario = LoadOffThePath().UnreadableBodies("Helper", before);

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.UnreadableMethodBodies);

        Assert.Equal(scenario.OutputPath("Helper"), whole.Subject);
    }

    [Fact]
    public async Task Unknown_assembly_next_to_the_tests_without_a_project_reference_keeps_the_project_whole()
    {
        using var scenario = BodyEdit()
            .Library("Plugin", Plugin, Plugin)
            .Neighbor("Tests", "Plugin")
            .Before("Plugin", path => AssemblySnapshotEntry.Unknown(path, "Rebuilt before its first read."));

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.BeforeSnapshotUnknown);

        Assert.Equal(scenario.OutputPath("Plugin"), whole.Subject);
    }

    [Fact]
    public async Task Output_assembly_not_newer_than_the_last_edit_is_not_rebuilt()
    {
        using var scenario = BodyEdit();
        scenario.Output("Lib", scenario.Edited);

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.NotRebuilt);

        Assert.Equal(scenario.OutputPath("Lib"), whole.Subject);
    }

    [Fact]
    public async Task Second_edit_without_a_build_is_not_rebuilt_although_fingerprints_differ_from_before()
    {
        // Edit A, build, edit B and do not build: the assembly holds the first edit, and is older than the second.
        using var scenario = new NarrowingScenario();
        scenario
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", TestClasses(ReferencesA, ReferencesB), TestClasses(ReferencesA, ReferencesB), "Lib")
            .EditAt("Lib", scenario.Edited, "A.cs")
            .EditAt("Lib", scenario.Built.AddMinutes(5), "B.cs");

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.NotRebuilt);

        Assert.Equal(scenario.OutputPath("Lib"), whole.Subject);
    }

    [Fact]
    public async Task Edited_document_that_no_longer_exists_keeps_the_project_whole()
    {
        using var scenario = BodyEdit().EditAt("Lib", null, "Gone.cs");

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.EditedDocumentMissing);

        Assert.Equal(scenario.DocumentPath("Lib", "Gone.cs"), whole.Subject);
    }

    [Fact]
    public async Task Output_assembly_that_no_longer_exists_is_not_rebuilt()
    {
        using var scenario = BodyEdit().Current("Lib", AssemblySnapshotEntry.Missing);

        NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.NotRebuilt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_a_reference_assembly_the_test_project_narrows_only_when_it_is_built_after_the_dependency(bool testsBuilt)
    {
        using var scenario = BodyEdit().WithoutReferenceAssembly("Lib");
        if (testsBuilt)
        {
            scenario.Output("Tests", scenario.Built.AddMinutes(1));
        }

        var project = await scenario.RunAsync();

        if (testsBuilt)
        {
            Assert.Equal(TestNarrowingOutcome.Narrowed, project.Outcome);
            return;
        }

        var whole = NarrowingAssert.Whole(project, TestNarrowingWholeKind.DependentNotRebuilt);
        Assert.Equal(scenario.OutputPath("Tests"), whole.Subject);
    }

    [Fact]
    public async Task With_a_reference_assembly_a_body_edit_narrows_when_only_the_dependency_is_built()
    {
        // The reference assembly keeps its time after a body edit; the test assembly is older than the library.
        using var scenario = BodyEdit();

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.Narrowed, project.Outcome);
        Assert.Equal(["Tests.ReferencesA"], NarrowingAssert.Names(project, TestClassStageLevel.References));
    }

    [Fact]
    public async Task Middle_project_not_rebuilt_after_its_dependency_keeps_the_test_project_whole()
    {
        using var scenario = new NarrowingScenario();
        scenario
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .Library("Mid", NarrowingSources.Mid, NarrowingSources.Mid, "Lib")
            .TestProject("Tests", TestClasses(UsesRelay), TestClasses(UsesRelay), "Mid")
            .WithoutReferenceAssembly("Lib")
            .Edit("Lib")
            .Output("Tests", scenario.Built.AddMinutes(1));

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.DependentNotRebuilt);

        Assert.Equal(scenario.OutputPath("Mid"), whole.Subject);
    }

    [Fact]
    public async Task Declared_reference_assembly_that_does_not_exist_keeps_the_project_whole()
    {
        using var scenario = BodyEdit().ReferenceAssembly("Lib", null);

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.ReferenceAssemblyMissing);

        Assert.Equal(scenario.ReferencePath("Lib"), whole.Subject);
    }

    [Theory]
    [InlineData(nameof(ProjectExpansionKind.NonCSharpInput), nameof(TestNarrowingWholeKind.NonCSharpInput))]
    [InlineData(nameof(ProjectExpansionKind.ProjectFile), nameof(TestNarrowingWholeKind.ProjectFile))]
    [InlineData(nameof(ProjectExpansionKind.ProjectGraphFile), nameof(TestNarrowingWholeKind.ProjectGraphStale))]
    [InlineData(nameof(ProjectExpansionKind.ObservationGap), nameof(TestNarrowingWholeKind.ObservationGap))]
    public async Task Project_selected_for_another_reason_than_a_CSharp_edit_stays_whole(string selectionName, string expectedName)
    {
        var selection = Enum.Parse<ProjectExpansionKind>(selectionName);
        var owner = selection is ProjectExpansionKind.NonCSharpInput or ProjectExpansionKind.ProjectFile ? "Lib" : null;
        var path = owner is null ? null : selection == ProjectExpansionKind.ProjectFile ? "Lib/Lib.csproj" : "Lib/data.json";
        using var scenario = BodyEdit().Expand("Tests", selection, owner, path);

        NarrowingAssert.Whole(await scenario.RunAsync(), Enum.Parse<TestNarrowingWholeKind>(expectedName));
    }

    [Fact]
    public async Task Stale_project_graph_keeps_the_project_whole()
    {
        using var scenario = BodyEdit().ProjectGraphStale();

        NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.ProjectGraphStale);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assembly_on_the_path_that_cannot_be_read_keeps_the_project_whole(bool before)
    {
        static AssemblySnapshotEntry Damaged(string path) => AssemblySnapshotEntry.Unreadable(
            path,
            new AssemblyFileStamp(10, DateTime.UtcNow),
            new IlUnreadAssembly(path, IlAssemblyFailureKind.NotAnAssembly, "Damaged."));
        using var scenario = BodyEdit();
        if (before)
        {
            scenario.Before("Lib", Damaged);
        }
        else
        {
            scenario.Current("Lib", Damaged);
        }

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.AssemblyUnreadable);

        Assert.Equal(scenario.OutputPath("Lib"), whole.Subject);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assembly_on_the_path_with_unread_method_bodies_keeps_the_project_whole(bool before)
    {
        using var scenario = BodyEdit().UnreadableBodies("Tests", before);

        var whole = NarrowingAssert.Whole(await scenario.RunAsync(), TestNarrowingWholeKind.UnreadableMethodBodies);

        Assert.Equal(scenario.OutputPath("Tests"), whole.Subject);
    }

    [Fact]
    public async Task Class_filter_over_the_planner_budget_runs_the_stage_whole()
    {
        string[] classes =
        [
            .. Enumerable.Range(0, 300).Select(index => $$"""
                    public sealed class ReferencesAWithANameLongEnoughToFillTheFilterBudgetQuickly{{index:000}}
                    {
                        [Fact]
                        public void Runs() => GC.KeepAlive(new Lib.A());
                    }
                """),
        ];
        var tests = TestClasses(classes);
        using var scenario = new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");

        var project = await scenario.RunAsync();

        Assert.Equal(TestNarrowingOutcome.Narrowed, project.Outcome);
        Assert.Null(project.Stage(TestClassStageLevel.Changed).Whole);
        var stage = project.Stage(TestClassStageLevel.References);
        Assert.Equal(300, stage.Classes.Count);
        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.FilterOverBudget, stage.Whole?.Kind);
    }

    [Fact]
    public async Task Selected_fixture_with_arguments_runs_the_stage_whole()
    {
        const string FixtureReason = "Runtime fixture constructor/source arguments require the whole project.";
        using var scenario = BodyEdit().Inventory(classes => classes.Select(testClass => testClass.RuntimeName == "Tests.ReferencesA"
            ? testClass with { RuntimeName = null, UnresolvedReason = FixtureReason, HasFixtureArguments = true }
            : testClass));

        var project = await scenario.RunAsync();

        Assert.Null(project.Stage(TestClassStageLevel.Changed).Whole);
        var stage = project.Stage(TestClassStageLevel.References);
        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.FixtureArguments, stage.Whole?.Kind);
    }

    [Fact]
    public async Task Selected_class_without_a_runtime_name_runs_the_stage_whole()
    {
        const string Reason = "Unknown or unmeasured closed generic runtime identity; run the whole project.";
        using var scenario = BodyEdit().Inventory(classes => classes.Select(testClass => testClass.RuntimeName == "Tests.ReferencesA"
            ? testClass with { RuntimeName = null, UnresolvedReason = Reason }
            : testClass));

        var stage = (await scenario.RunAsync()).Stage(TestClassStageLevel.References);

        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, stage.Whole?.Kind);
        Assert.Equal(Reason, stage.Whole?.Message);
    }

    [Fact]
    public async Task Test_class_of_the_current_graph_missing_from_the_inventory_runs_the_stage_whole()
    {
        using var scenario = BodyEdit().Inventory(classes => classes.Where(testClass => testClass.RuntimeName != "Tests.ReferencesA"));

        var project = await scenario.RunAsync();

        Assert.Null(project.Stage(TestClassStageLevel.Changed).Whole);
        var stage = project.Stage(TestClassStageLevel.References);
        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, stage.Whole?.Kind);
        Assert.Equal(new IlTypeName("Tests", "Tests.ReferencesA").ToString(), stage.Whole?.Subject);
    }

    [Fact]
    public async Task Changed_test_of_epoch_1_without_an_inventory_class_runs_the_changed_stage_whole()
    {
        using var scenario = BodyEdit()
            .ChangedTest("Tests", "Tests.ReferencesB", "Other")
            .Inventory(classes => classes.Where(testClass => testClass.RuntimeName != "Tests.ReferencesB"));

        var stage = (await scenario.RunAsync()).Stage(TestClassStageLevel.Changed);

        Assert.Null(stage.Filter);
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, stage.Whole?.Kind);
    }

    /// <summary>
    /// A body edit of <c>A</c> built with the library alone, as after a build of the library project; the test
    /// assembly is not recompiled and keeps its time.
    /// </summary>
    /// <summary>A removed library type that a test reaches only through a load by name in another library.</summary>
    private static NarrowingScenario LoadOffThePath()
    {
        var tests = TestClasses(UsesHelperLoader, ReferencesB);
        return new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public sealed class OldHandler : IService { }", ""))
            .Library("Helper", HelperWithLoader, HelperWithLoader)
            .TestProject("Tests", tests, tests, "Lib", "Helper")
            .Edit("Lib");
    }

    private static NarrowingScenario BodyEdit()
    {
        var tests = TestClasses(ReferencesA, ReferencesB);
        return new NarrowingScenario()
            .Library("Lib", Lib, LibWith("public int Run() => 1;", "public int Run() => 10;"))
            .TestProject("Tests", tests, tests, "Lib")
            .Edit("Lib");
    }
}
