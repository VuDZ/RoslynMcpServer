using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class EstimateAffectedTestsTests
{
    [Fact]
    public async Task No_workspace_and_cancelled_responses_begin_with_the_warning()
    {
        var manager = SolutionManagerTestFactory.Create();
        var tools = Tools(manager);
        var unloaded = await tools.EstimateAffectedTests();
        AssertResponse(unloaded);
        Assert.Contains("No workspace loaded", unloaded, StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await tools.EstimateAffectedTests(cancellationToken: cancellation.Token);
        AssertResponse(cancelled);
        Assert.Contains("cancelled", cancelled, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_result_has_warning_zero_changes_not_covered_and_project_level_state()
    {
        await using var fixture = await LoadAsync();
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        AssertResponse(result);
        Assert.Contains("Changed since load: 0 members in 0 files", result, StringComparison.Ordinal);
        Assert.Contains("Not covered: none", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_MSBuild_disk_and_Mcp_edits_select_the_dependent_test_project(bool mcp)
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = fixture.DeletedSourcePath;
        const string source = "class Old { void Run() { System.GC.KeepAlive(1); } }";
        if (mcp)
        {
            var editing = new EditingTools(NullLogger<EditingTools>.Instance, fixture.Manager);
            Assert.Contains("Successfully wrote", await editing.WriteFile(path, source), StringComparison.Ordinal);
        }
        else
        {
            await File.WriteAllTextAsync(path, source);
            Assert.True((await fixture.Manager.WaitForDirtySourceAsync(path, TimeSpan.FromSeconds(15))).Delivered);
        }
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        AssertResponse(result);
        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Project expansion", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Isolated.csproj", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_MSBuild_inherited_only_test_project_is_selected_after_base_or_production_edit(bool editBase)
    {
        await using var fixture = new TestImpactWatcherFixture();
        var basePath = Path.Combine(fixture.Files.Root, "Library", "Base.cs");
        const string source = "public class FactAttribute : System.Attribute {} public abstract class Base { [Fact] public void Inherited() {} }";
        fixture.Files.ReplaceSource(fixture.Files.Library, basePath, source);
        fixture.Files.ReplaceSource(fixture.Files.Specs, fixture.Files.SpecsPath, "public class Derived : Base {}");
        fixture.Files.AddProjectReference(fixture.Files.Specs, fixture.Files.Library);
        await fixture.LoadAsync();
        var specs = fixture.Solution.Projects.Single(project => project.Name == "Specs");
        Assert.DoesNotContain(specs.Documents, document => document.FilePath == basePath);
        Assert.Contains(await VstestTestInventory.BuildAsync(fixture.Solution, CancellationToken.None),
            testClass => testClass.ProjectId == specs.Id && testClass.RuntimeName == "Derived"
                && testClass.Tests.Any(key => key.Name == "Inherited"));
        var editing = new EditingTools(NullLogger<EditingTools>.Instance, fixture.Manager);
        var path = editBase ? basePath : fixture.DeletedSourcePath;
        var changed = editBase
            ? source.Replace("Inherited() {}", "Inherited() { System.GC.KeepAlive(1); }", StringComparison.Ordinal)
            : "class Old { void Run() { System.GC.KeepAlive(1); } }";
        Assert.Contains("Successfully wrote", await editing.WriteFile(path, changed), StringComparison.Ordinal);
        var result = await Tools(fixture.Manager).EstimateAffectedTests();
        AssertResponse(result);
        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Isolated.csproj", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Details_list_all_changed_members_and_tests_while_default_is_a_summary()
    {
        await using var fixture = await LoadAsync();
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, TestsSource.Replace("First() {}", "First() { System.GC.KeepAlive(1); }", StringComparison.Ordinal));
        var tools = Tools(fixture.Manager);
        var summary = await tools.EstimateAffectedTests();
        var details = await tools.EstimateAffectedTests(includeDetails: true);

        AssertResponse(summary);
        AssertResponse(details);
        Assert.Contains("FullyQualifiedName~Ns.Cases.First", summary, StringComparison.Ordinal);
        Assert.Contains("Changed test", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Changed members:", summary, StringComparison.Ordinal);
        Assert.Contains("Changed members:", details, StringComparison.Ordinal);
        Assert.Contains("Selected tests:", details, StringComparison.Ordinal);
        Assert.Contains("Cases`0.First", details, StringComparison.Ordinal);
        Assert.Contains(fixture.FirstPath, details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Details_of_project_expansion_list_every_current_test_in_the_selected_project()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var editing = new EditingTools(NullLogger<EditingTools>.Instance, fixture.Manager);
        Assert.Contains("Successfully wrote", await editing.WriteFile(fixture.DeletedSourcePath,
            "class Old { void Run() { System.GC.KeepAlive(1); } }"), StringComparison.Ordinal);
        var result = await Tools(fixture.Manager).EstimateAffectedTests(includeDetails: true);

        Assert.Contains("Selected tests:", result, StringComparison.Ordinal);
        Assert.Contains("Specs: Cases`0.Case", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Isolated: Cases`0.Case", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restored_source_and_trivia_only_edits_are_unchanged()
    {
        await using var fixture = await LoadAsync();
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, TestsSource.Replace("First() {}", "First() { System.GC.KeepAlive(1); }", StringComparison.Ordinal));
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, TestsSource + "\n// Comment only.\n");
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Changed since load: 0 members in 0 files", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Semantic_compile_errors_in_changed_documents_are_reported_with_the_path()
    {
        await using var fixture = await LoadAsync();
        await fixture.WriteThroughMcpAsync(fixture.FirstPath,
            TestsSource.Replace("First() {}", "First() { MissingSymbol(); }", StringComparison.Ordinal));
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        AssertResponse(result);
        Assert.Contains("Compilation errors", result, StringComparison.Ordinal);
        Assert.Contains("CS0103", result, StringComparison.Ordinal);
        Assert.Contains(fixture.FirstPath, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("App.csproj", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pending_baseline_is_unknown_and_project_expanded_even_before_capture_finishes()
    {
        await using var fixture = new BaselineLifecycleFixture();
        WriteBaseline(fixture.FirstPath, TestsSource);
        WriteBaseline(fixture.SecondPath, "class Other {}");
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, TestsSource + " class Added {}");
        var result = await Tools(fixture.Manager).EstimateAffectedTests();
        gate.Release();

        AssertResponse(result);
        Assert.Contains("baseline unknown", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fixture.FirstPath, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Observation_gap_expands_projects_even_when_known_CSharp_is_unchanged_and_survives_reload()
    {
        await using var fixture = await LoadAsync();
        fixture.Manager.Baseline.NoteObservationGap();
        foreach (var reload in new[] { false, true })
        {
            if (reload)
            {
                await fixture.Manager.LoadAsync(fixture.ProjectPath);
            }
            var result = await Tools(fixture.Manager).EstimateAffectedTests();
            AssertResponse(result);
            Assert.Contains("observation gap", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("non-C#", result, StringComparison.Ordinal);
            Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Native_new_file_requires_reload_without_selecting_independent_projects()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Files.Root, "Specs", "Fresh.cs");
        await File.WriteAllTextAsync(path, "class Fresh { [Fact] void NewTest() {} }");
        await fixture.WaitForEventAsync(path);
        var before = await Tools(fixture.Manager).EstimateAffectedTests(includeDetails: true);

        AssertResponse(before);
        Assert.Contains("Specs.csproj", before, StringComparison.Ordinal);
        Assert.Contains("NewTest", before, StringComparison.Ordinal);
        Assert.Contains("reload required", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Isolated.csproj", before, StringComparison.Ordinal);

        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        var after = await Tools(fixture.Manager).EstimateAffectedTests(includeDetails: true);
        Assert.Contains("NewTest", after, StringComparison.Ordinal);
        Assert.DoesNotContain("reload required", after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Native_deleted_known_source_selects_its_dependent_tests_before_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        Assert.Contains(fixture.Manager.Baseline.GetSnapshot().Documents, document =>
            InputPathCanon.Comparer.Equals(document.Key.DocumentPath, fixture.DeletedSourcePath)
                && document.Status == Services.Testing.Baseline.Models.BaselineDocumentStatus.Known);
        File.Delete(fixture.DeletedSourcePath);
        await fixture.WaitForEventAsync(fixture.DeletedSourcePath);
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        AssertResponse(result);
        Assert.Contains("reload required", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Isolated.csproj", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Native_deleted_source_does_not_recommend_a_second_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        File.Delete(fixture.DeletedSourcePath);
        await fixture.WaitForEventAsync(fixture.DeletedSourcePath);
        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        AssertResponse(result);
        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.DoesNotContain("reload required", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Native_unmapped_path_is_not_covered_and_graph_file_changes_select_every_test_project()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Files.Root, "Specs", "notes.txt");
        await File.WriteAllTextAsync(path, "Notes.");
        await fixture.WaitForEventAsync(path);
        var uncovered = await Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains("Not covered:", uncovered, StringComparison.Ordinal);
        Assert.Contains(path, uncovered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Specs.csproj", uncovered, StringComparison.Ordinal);

        path = Path.Combine(fixture.Files.Root, "Directory.Build.props");
        await File.WriteAllTextAsync(path, "<Project />");
        await fixture.WaitForEventAsync(path);
        var stale = await Tools(fixture.Manager).EstimateAffectedTests();
        AssertResponse(stale);
        Assert.Contains("project graph stale", stale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reload required", stale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Specs.csproj", stale, StringComparison.Ordinal);
        Assert.Contains("Isolated.csproj", stale, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Baseline_and_current_base_names_both_prevent_unsafe_narrowing(bool removeDerived)
    {
        const string baseTests = "class FactAttribute : System.Attribute {} class Base { [Fact] public void Check() {} }";
        await using var fixture = await LoadAsync(baseTests, removeDerived ? "class Derived : Base {}" : "class Other {}");
        if (removeDerived)
        {
            await fixture.WriteThroughMcpAsync(fixture.SecondPath, "class Other {}");
        }
        else
        {
            await fixture.WriteThroughMcpAsync(fixture.SecondPath, "class Derived : Base {}");
        }
        await fixture.WriteThroughMcpAsync(fixture.FirstPath,
            baseTests.Replace("Check() {}", "Check() { System.GC.KeepAlive(1); }", StringComparison.Ordinal));
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partial_class_other_document_prevents_collapsing_one_selected_test_into_the_whole_class()
    {
        const string source = "class FactAttribute : System.Attribute {} namespace Ns { public partial class Cases { [Fact] public void First() {} } }";
        await using var fixture = await LoadAsync(source, "namespace Ns { public partial class Cases { [Fact] public void Second() {} } }");
        await fixture.WriteThroughMcpAsync(fixture.FirstPath,
            source.Replace("First() {}", "First() { System.GC.KeepAlive(1); }", StringComparison.Ordinal));
        var result = await Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("FullyQualifiedName~Ns.Cases.First", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~Ns.Cases.`", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~Ns.Cases.Second", result, StringComparison.Ordinal);
    }

    internal static TestTools Tools(SolutionManager manager) => new(manager, NullLogger<TestTools>.Instance);

    internal static void AssertResponse(string response)
    {
        Assert.StartsWith(Warning + Environment.NewLine, response, StringComparison.Ordinal);
        Assert.Contains("project-level result only", response, StringComparison.Ordinal);
    }

    private static async Task<BaselineLifecycleFixture> LoadAsync(string? source = null, string second = "class Other {}")
    {
        var fixture = new BaselineLifecycleFixture();
        WriteBaseline(fixture.FirstPath, source ?? TestsSource);
        WriteBaseline(fixture.SecondPath, second);
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(fixture.Manager.Baseline.GetSnapshot().Documents, document =>
            Assert.Equal(Services.Testing.Baseline.Models.BaselineDocumentStatus.Known, document.Status));
        return fixture;
    }

    private static void WriteBaseline(string path, string source)
    {
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
    }

    internal const string Warning = "Approximate result (rapid TIA): not a substitute for the full test suite or CI.";
    private const string TestsSource = "class FactAttribute : System.Attribute {} namespace Ns { public class Cases { [Fact] public void First() {} [Fact] public void Second() {} } }";
}
