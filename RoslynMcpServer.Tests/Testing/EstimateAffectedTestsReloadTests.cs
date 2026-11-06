using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class EstimateAffectedTestsReloadTests
{
    [Fact]
    public async Task Native_new_source_reload_returns_to_filters_without_independent_projects()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        var original = await File.ReadAllTextAsync(fixture.Files.SpecsPath);
        await EditTestAsync(fixture, original, 1);
        var fresh = Path.Combine(fixture.Files.Root, "Specs", "Fresh.cs");
        await File.WriteAllTextAsync(fresh, "using System; namespace Added { class Fresh { [Fact] void NewTest() {} } }");
        await fixture.WaitForEventAsync(fresh);
        var before = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains("reload required", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Isolated.csproj", before, StringComparison.Ordinal);

        await ReloadAsync(fixture);
        var after = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests(includeDetails: true);
        AssertFilteredSpecs(after);
        Assert.Contains("NewTest", after, StringComparison.Ordinal);
        AssertReloadUncertainty(after);
        Assert.DoesNotContain("reload required", after, StringComparison.OrdinalIgnoreCase);

        await EditTestAsync(fixture, original, 2);
        var later = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        AssertFilteredSpecs(later);
        AssertReloadUncertainty(later);
    }

    [Fact]
    public async Task Native_new_source_deleted_and_test_restored_have_an_empty_plan_after_real_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        var original = await File.ReadAllTextAsync(fixture.Files.SpecsPath);
        await EditTestAsync(fixture, original, 1);
        var fresh = Path.Combine(fixture.Files.Root, "Specs", "Fresh.cs");
        await File.WriteAllTextAsync(fresh, "class Fresh { [Fact] void NewTest() {} }");
        await fixture.WaitForEventAsync(fresh);
        await ReloadAsync(fixture);

        await File.WriteAllTextAsync(fixture.Files.SpecsPath, original);
        Assert.True((await fixture.Manager.WaitForDirtySourceAsync(fixture.Files.SpecsPath, _timeout)).Delivered);
        File.Delete(fresh);
        await fixture.WaitForEventAsync(fresh);
        await ReloadAsync(fixture);
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(result);
        Assert.True(!result.Contains("Run:", StringComparison.Ordinal), result);
        Assert.Contains("Changed since load: 0 members in 0 files", result, StringComparison.Ordinal);
        AssertReloadUncertainty(result);
    }

    [Fact]
    public async Task Native_project_edit_reload_keeps_its_owner_closure_without_independent_projects()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        var projectPath = await ChangeProjectAsync(fixture);
        var before = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains("Isolated.csproj", before, StringComparison.Ordinal);
        Assert.Contains("project graph stale", before, StringComparison.OrdinalIgnoreCase);
        await ReloadAsync(fixture);
        var after = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(after);
        Assert.Contains("Specs.csproj", after, StringComparison.Ordinal);
        Assert.Contains("whole project", after, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Isolated.csproj", after, StringComparison.Ordinal);
        Assert.DoesNotContain("reload required", after, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(projectPath, after, StringComparison.OrdinalIgnoreCase);
        AssertReloadUncertainty(after);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload(bool inWindow)
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        var original = await File.ReadAllTextAsync(fixture.Files.SpecsPath);
        var initial = fixture.Manager.Baseline.GetSnapshot();
        var known = Assert.Single(initial.Documents, document =>
            InputPathCanon.Comparer.Equals(document.Key.DocumentPath, fixture.Files.SpecsPath));
        Assert.Equal(BaselineDocumentStatus.Known, known.Status);
        await fixture.Manager.ClearWorkspaceAsync();
        if (inWindow)
        {
            fixture.Manager.BeforeDiskWatcherStartForTests = () =>
            {
                File.WriteAllText(fixture.Files.SpecsPath, ChangedTest(original, 1));
                File.SetLastWriteTimeUtc(fixture.Files.SpecsPath, initial.PublishedAtUtc.AddDays(-1));
            };
        }
        try
        {
            await ReloadAsync(fixture);
        }
        finally
        {
            fixture.Manager.BeforeDiskWatcherStartForTests = null;
        }
        var after = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(after.RequiresFullScan);
        Assert.Contains(fixture.Files.SpecsPath, after.TouchedPaths, InputPathCanon.Comparer);
        Assert.Same(known.Snapshot, Assert.Single(after.Documents, document => document.Key == known.Key).Snapshot);
        if (inWindow)
        {
            Assert.Equal(0, fixture.Manager.DiskWatcherSession!.EventRevision(fixture.Files.SpecsPath));
        }
        else
        {
            await EditTestAsync(fixture, original, 2);
        }
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        AssertFilteredSpecs(result);
        AssertReloadUncertainty(result);
    }

    [Fact]
    public async Task True_observation_gap_still_expands_every_project_after_a_real_native_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        fixture.Manager.Baseline.NoteObservationGap();
        await ChangeProjectAsync(fixture);
        await ReloadAsync(fixture);
        Assert.True(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.Contains("Isolated.csproj", result, StringComparison.Ordinal);
        Assert.Contains("Observation gap", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FullyQualifiedName~", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_capture_failure_remains_a_true_gap_after_a_real_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        fixture.Manager.Baseline.BeforeReadForTests = (_, _) => throw new InvalidOperationException("Capture failure probe.");
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        fixture.Manager.Baseline.BeforeReadForTests = null;
        await ChangeProjectAsync(fixture);
        await ReloadAsync(fixture);
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.Contains("Isolated.csproj", result, StringComparison.Ordinal);
        Assert.Contains("Observation gap", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FullyQualifiedName~", result, StringComparison.Ordinal);
    }

    private static async Task ReloadAsync(TestImpactWatcherFixture fixture)
    {
        var generation = fixture.Manager.Baseline.GetSnapshot().Generation;
        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        Assert.True(fixture.Manager.LastLoadReopenedGraph, "The test must reopen MSBuild, not reuse the load cache.");
        Assert.False(fixture.Manager.LastLoadWasCacheHit);
        Assert.True(fixture.Manager.Baseline.GetSnapshot().Generation > generation);
        Assert.False(fixture.Manager.ProjectGraphStale);
    }

    private static async Task<string> ChangeProjectAsync(TestImpactWatcherFixture fixture)
    {
        var projectPath = fixture.Solution.Projects.Single(project => project.Name == "Specs").FilePath!;
        var source = await File.ReadAllTextAsync(projectPath);
        await File.WriteAllTextAsync(projectPath, source.Replace("</Project>",
            "<PropertyGroup><DefineConstants>RELOAD_PROBE</DefineConstants></PropertyGroup></Project>", StringComparison.Ordinal));
        await fixture.WaitForEventAsync(projectPath);
        return projectPath;
    }

    private static async Task EditTestAsync(TestImpactWatcherFixture fixture, string original, int value)
    {
        await File.WriteAllTextAsync(fixture.Files.SpecsPath, ChangedTest(original, value));
        Assert.True((await fixture.Manager.WaitForDirtySourceAsync(fixture.Files.SpecsPath, _timeout)).Delivered);
    }

    private static string ChangedTest(string original, int value) =>
        original.Replace("Case() {}", "Case() { System.GC.KeepAlive(" + value + "); }", StringComparison.Ordinal);

    private static void AssertFilteredSpecs(string result)
    {
        EstimateAffectedTestsTests.AssertResponse(result);
        Assert.Contains("Specs.csproj", result, StringComparison.Ordinal);
        Assert.True(result.Contains("FullyQualifiedName~", StringComparison.Ordinal), result);
        Assert.DoesNotContain("whole project", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Isolated.csproj", result, StringComparison.Ordinal);
    }

    private static void AssertReloadUncertainty(string result)
    {
        Assert.Contains("Not covered:", result, StringComparison.Ordinal);
        Assert.Contains("non-C#", result, StringComparison.Ordinal);
        Assert.Contains("reload", result, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
