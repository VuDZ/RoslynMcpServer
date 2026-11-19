using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class EstimateAffectedTestsNonSourceReloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleted_original_globbed_AdditionalFile_with_cs_extension_remains_selected_after_real_reload(
        bool compileOnIntermediateReload)
    {
        await using var fixture = new BaselineLifecycleFixture();
        File.WriteAllText(fixture.FirstPath,
            "class FactAttribute : System.Attribute {} class Cases { [Fact] void Case() {} }");
        File.WriteAllText(fixture.SecondPath, "class Other {}");
        File.SetLastWriteTimeUtc(fixture.FirstPath, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(fixture.SecondPath, DateTime.UtcNow.AddDays(-1));
        var dataDirectory = Path.Combine(fixture.Root, "Data");
        Directory.CreateDirectory(dataDirectory);
        var payload = Path.Combine(dataDirectory, "Payload.cs");
        await File.WriteAllTextAsync(payload, "class Payload {}");
        var projectText = (await File.ReadAllTextAsync(fixture.ProjectPath)).Replace("</Project>",
            "<ItemGroup><AdditionalFiles Include=\"Data/*.cs\" />"
            + "<Compile Include=\"Data/*.cs\" Condition=\"'$(Configuration)' == 'Release'\" /></ItemGroup></Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.ProjectPath, projectText);
        var solution = await fixture.Manager.LoadAsync(fixture.ProjectPath, CancellationToken.None, configuration: "Debug");
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var project = Assert.Single(solution.Projects);
        Assert.Single(project.AdditionalDocuments, document => InputPathCanon.Comparer.Equals(document.FilePath, payload));
        Assert.DoesNotContain(project.Documents, document => InputPathCanon.Comparer.Equals(document.FilePath, payload));
        var originalEntry = fixture.Manager.DiskWatcherSession!.InputMap!.Find(payload);
        Assert.NotNull(originalEntry);
        Assert.All(originalEntry.Occurrences, occurrence => Assert.Equal(InputRole.AdditionalFile, occurrence.Role));
        Assert.DoesNotContain(fixture.Manager.Baseline.GetSnapshot().Documents,
            document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, payload));
        var initial = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.DoesNotContain("Run:", initial, StringComparison.Ordinal);
        if (compileOnIntermediateReload)
        {
            await fixture.Manager.LoadAsync(fixture.ProjectPath, CancellationToken.None, configuration: "Release");
            Assert.True(fixture.Manager.LastLoadReopenedGraph);
            Assert.False(fixture.Manager.LastLoadWasCacheHit);
            Assert.Contains(fixture.Manager.GetCurrentSolution()!.Projects.SelectMany(current => current.Documents),
                document => InputPathCanon.Comparer.Equals(document.FilePath, payload));
        }

        File.Delete(payload);
        await WaitForNativeEventAsync(fixture.Manager, payload);
        var before = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains("App.csproj", before, StringComparison.Ordinal);
        Assert.Contains("whole project", before, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(payload, before, StringComparison.OrdinalIgnoreCase);
        var generation = fixture.Manager.Baseline.GetSnapshot().Generation;
        await fixture.Manager.LoadAsync(fixture.ProjectPath, CancellationToken.None,
            configuration: compileOnIntermediateReload ? "Debug" : "Release");
        Assert.True(fixture.Manager.LastLoadReopenedGraph);
        Assert.False(fixture.Manager.LastLoadWasCacheHit);
        Assert.True(fixture.Manager.Baseline.GetSnapshot().Generation > generation);
        Assert.False(fixture.Manager.Baseline.GetSnapshot().HasObservationGap);
        Assert.Equal(projectText, await File.ReadAllTextAsync(fixture.ProjectPath));
        Assert.Null(fixture.Manager.DiskWatcherSession!.InputMap!.Find(payload));
        Assert.Empty(fixture.Manager.GetCurrentSolution()!.Projects.SelectMany(current => current.AdditionalDocuments));
        Assert.Contains(payload, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        var after = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(after);
        Assert.True(after.Contains("App.csproj", StringComparison.Ordinal), after);
        Assert.Contains("whole project", after, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(payload, after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_recorded_unknown_journal_path_stays_not_covered_after_real_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var path = Path.Combine(fixture.Files.Root, "Unmapped.cs");
        await File.WriteAllTextAsync(path, "Unmapped input.");
        await fixture.LoadAsync(suppressGeneratedFiles: true);
        Assert.Null(fixture.Manager.DiskWatcherSession!.InputMap!.Find(path));
        var editing = new EditingTools(NullLogger<EditingTools>.Instance, fixture.Manager);
        Assert.Contains("Successfully wrote", await editing.WriteFile(path, "Unmapped input. Changed."), StringComparison.Ordinal);
        Assert.Contains(path, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        var before = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains("Not covered:", before, StringComparison.Ordinal);
        Assert.Contains(path, before, StringComparison.OrdinalIgnoreCase);

        File.Delete(path);
        var generation = fixture.Manager.Baseline.GetSnapshot().Generation;
        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath, CancellationToken.None, configuration: "Release");
        Assert.True(fixture.Manager.LastLoadReopenedGraph);
        Assert.False(fixture.Manager.LastLoadWasCacheHit);
        Assert.True(fixture.Manager.Baseline.GetSnapshot().Generation > generation);
        Assert.False(fixture.Manager.Baseline.GetSnapshot().HasObservationGap);
        Assert.Contains(path, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        var after = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(after);
        Assert.True(after.Contains(path, StringComparison.OrdinalIgnoreCase), after);
        Assert.Contains("Path outside the loaded input map", after, StringComparison.Ordinal);
    }

    private static async Task WaitForNativeEventAsync(SolutionManager manager, string path, int previousRevision = 0)
    {
        var stopwatch = Stopwatch.StartNew();
        while (manager.DiskWatcherSession!.EventRevision(path) <= previousRevision && stopwatch.Elapsed < _timeout)
        {
            await Task.Delay(20);
        }
        Assert.True(manager.DiskWatcherSession!.EventRevision(path) > previousRevision,
            "The native watcher did not observe " + path);
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
