using RoslynMcpServer.Services.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class RebuildInputImpactTests
{
    [Theory]
    [InlineData(Config)]
    [InlineData(ChangedConfig)]
    [InlineData(null)]
    public async Task TIA_ignores_only_analyzer_config_that_matches_the_first_load(string? current)
    {
        await using var fixture = new TestImpactWatcherFixture();
        var configPath = CreateConfig(fixture);
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        AssertConfigRole(fixture, configPath);
        var session = fixture.Manager.DiskWatcherSession!;
        var inputRevision = session.InputRevisionForPath(configPath);

        if (current is null)
        {
            File.Delete(configPath);
            Notify(fixture, configPath, WatcherChangeTypes.Deleted);
        }
        else
        {
            File.WriteAllText(configPath, current);
            Notify(fixture, configPath, WatcherChangeTypes.Changed);
        }

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();
        Assert.Contains(configPath, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        Assert.True(session.InputRevisionForPath(configPath) > inputRevision);
        Assert.False(fixture.Manager.ProjectGraphStale);
        if (current == Config)
        {
            AssertEmptyPlan(result);
        }
        else
        {
            Assert.Contains("Non-C# input changed:", result, StringComparison.Ordinal);
            Assert.Contains("Specs.csproj` whole project.", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Analyzer_config_notified_before_publication_is_not_a_trusted_fingerprint()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var configPath = CreateConfig(fixture);
        fixture.Manager.AfterDiskWatcherStartForTests = () =>
        {
            File.WriteAllText(configPath, ChangedConfig);
            File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddDays(-1));
            Notify(fixture, configPath, WatcherChangeTypes.Changed);
        };
        await fixture.LoadAsync();
        AssertConfigRole(fixture, configPath);

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Non-C# input changed:", result, StringComparison.Ordinal);
        Assert.Contains(configPath, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyzer_config_written_after_watcher_start_stays_changed_when_the_callback_arrives_after_publication()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var configPath = CreateConfig(fixture);
        var originalTimestamp = File.GetLastWriteTimeUtc(configPath);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task? callback = null;
        fixture.Manager.BeforeDiskWatcherCallbackForTests = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15)), "The callback must be released.");
        };
        fixture.Manager.AfterDiskWatcherStartForTests = () =>
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            File.WriteAllText(configPath, ChangedConfig);
            File.SetLastWriteTimeUtc(configPath, originalTimestamp);
            callback = Task.Run(() => Notify(fixture, configPath, WatcherChangeTypes.Changed));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)), "The callback must reach admission.");
        };

        try
        {
            await fixture.LoadAsync();
            AssertConfigRole(fixture, configPath);
            var initial = fixture.Manager.Baseline.GetSnapshot();
            Assert.DoesNotContain(configPath, initial.TouchedPaths, InputPathCanon.Comparer);
        }
        finally
        {
            release.Set();
            if (callback is not null)
            {
                await callback;
            }
            fixture.Manager.AfterDiskWatcherStartForTests = null;
            fixture.Manager.BeforeDiskWatcherCallbackForTests = null;
        }

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Non-C# input changed: " + configPath, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyzer_config_with_a_pending_revision_is_not_suppressed_until_reconciled()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var configPath = CreateConfig(fixture);
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        AssertConfigRole(fixture, configPath);
        Notify(fixture, configPath, WatcherChangeTypes.Changed);
        var session = fixture.Manager.DiskWatcherSession!;
        fixture.Manager.DuringContentReadForTests = path =>
        {
            if (InputPathCanon.Comparer.Equals(path, configPath))
            {
                session.NotePendingContent(path);
            }
        };

        var tools = EstimateAffectedTestsTests.Tools(fixture.Manager);
        try
        {
            Assert.Contains("Non-C# input changed:", await tools.EstimateAffectedTests(), StringComparison.Ordinal);
        }
        finally
        {
            fixture.Manager.DuringContentReadForTests = null;
        }

        AssertEmptyPlan(await tools.EstimateAffectedTests());
    }

    [Fact]
    public async Task Analyzer_config_restored_to_MCP_written_bytes_also_restores_published_text()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var path = CreateConfig(fixture);
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        AssertConfigRole(fixture, path);
        await fixture.Manager.WriteTrackedTextAsync(
            path, Config, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CancellationToken.None);
        File.WriteAllText(path, ChangedConfig);
        Notify(fixture, path, WatcherChangeTypes.Changed);
        await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.Equal(ChangedConfig, await PublishedConfigTextAsync(fixture, path));

        File.WriteAllText(path, Config);
        Notify(fixture, path, WatcherChangeTypes.Changed);
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        AssertEmptyPlan(result);
        Assert.Equal(Config, await PublishedConfigTextAsync(fixture, path));
    }

    [Fact]
    public async Task Analyzer_config_reverted_on_disk_stays_changed_until_the_published_solution_matches()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var path = CreateConfig(fixture);
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        AssertConfigRole(fixture, path);
        File.WriteAllText(path, ChangedConfig);
        Notify(fixture, path, WatcherChangeTypes.Changed);
        await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.Equal(ChangedConfig, await PublishedConfigTextAsync(fixture, path));

        var tools = EstimateAffectedTestsTests.Tools(fixture.Manager);
        fixture.Manager.DuringDiskFlushForTests = () => File.WriteAllText(path, Config);
        try
        {
            var estimate = await tools.EstimateAffectedTests();
            Assert.Contains("Non-C# input changed: " + path, estimate, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ChangedConfig, await PublishedConfigTextAsync(fixture, path));
        }
        finally
        {
            fixture.Manager.DuringDiskFlushForTests = null;
        }

        Notify(fixture, path, WatcherChangeTypes.Changed);
        AssertEmptyPlan(await tools.EstimateAffectedTests());
        Assert.Equal(Config, await PublishedConfigTextAsync(fixture, path));
    }

    [Fact]
    public async Task Analyzer_config_reclassified_as_also_an_additional_file_is_not_suppressed_after_reload()
    {
        await using var fixture = new TestImpactWatcherFixture();
        var configPath = CreateConfig(fixture);
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        AssertConfigRole(fixture, configPath);
        var projectPath = fixture.Solution.Projects.Single(project => project.Name == "Specs").FilePath!;
        var projectText = File.ReadAllText(projectPath).Replace(
            "</Project>", "<ItemGroup><AdditionalFiles Include=\".editorconfig\" /></ItemGroup></Project>", StringComparison.Ordinal);
        File.WriteAllText(projectPath, projectText);
        Notify(fixture, projectPath, WatcherChangeTypes.Changed);
        await fixture.Manager.LoadAsync(fixture.Files.SolutionPath);
        fixture.Manager.SilenceDiskWatchersForTests();
        var entry = fixture.Manager.DiskWatcherSession!.InputMap!.Find(configPath);
        Assert.NotNull(entry);
        Assert.Contains(entry.Occurrences, occurrence => occurrence.Role == InputRole.AnalyzerConfig);
        Assert.Contains(entry.Occurrences, occurrence => occurrence.Role == InputRole.AdditionalFile);
        Notify(fixture, configPath, WatcherChangeTypes.Changed);

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Contains("Non-C# input changed: " + configPath, result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("restored")]
    [InlineData("absent")]
    [InlineData("graph")]
    [InlineData("new source")]
    [InlineData("overflow")]
    public async Task Restoring_source_settles_only_its_own_provisional_absence(string operation)
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = fixture.Files.SpecsPath;
        var original = File.ReadAllBytes(path);
        File.Delete(path);
        Notify(fixture, path, WatcherChangeTypes.Deleted);
        Assert.True(fixture.Manager.ProjectGraphStaleFromComposition);

        if (operation != "absent")
        {
            File.WriteAllBytes(path, original);
            Notify(fixture, path, WatcherChangeTypes.Created);
        }
        if (operation == "graph")
        {
            var projectPath = fixture.Solution.Projects.Single(project => project.Name == "Specs").FilePath!;
            File.AppendAllText(projectPath, Environment.NewLine);
            Notify(fixture, projectPath, WatcherChangeTypes.Changed);
        }
        if (operation == "new source")
        {
            var newSource = Path.Combine(Path.GetDirectoryName(path)!, "NewSource.cs");
            File.WriteAllText(newSource, "class NewSource {}");
            Notify(fixture, newSource, WatcherChangeTypes.Created);
        }
        if (operation == "overflow")
        {
            fixture.Manager.NotifyDiskWatcherError(
                fixture.Manager.DiskWatcherSession!,
                new InternalBufferOverflowException());
        }

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Equal(operation == "graph", fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.Equal(operation is "absent" or "new source" or "overflow",
            fixture.Manager.ProjectGraphStaleFromComposition);
        Assert.Equal(operation == "overflow", fixture.Manager.Baseline.GetSnapshot().HasObservationGap);
        if (operation == "restored")
        {
            AssertEmptyPlan(result);
        }
        else
        {
            Assert.Contains("Project graph stale", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Restored_source_with_different_bytes_reaches_TIA_and_the_loaded_document()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = fixture.Files.SpecsPath;
        var changed = File.ReadAllText(path).Replace(
            "void Case() {}", "void Case() { System.Console.WriteLine(1); }", StringComparison.Ordinal);
        File.Delete(path);
        Notify(fixture, path, WatcherChangeTypes.Deleted);
        File.WriteAllText(path, changed);
        Notify(fixture, path, WatcherChangeTypes.Created);

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.Contains("Changed since load: 1 members in 1 files", result, StringComparison.Ordinal);
        Assert.Equal(changed, await PublishedSourceTextAsync(fixture, path));
    }

    [Fact]
    public async Task Restored_source_matching_an_earlier_MCP_write_still_refreshes_the_loaded_text()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = fixture.Files.SpecsPath;
        var original = File.ReadAllText(path);
        var owned = original.Replace("void Case() {}", "void Case() { System.Console.WriteLine(1); }", StringComparison.Ordinal);
        await fixture.Manager.UpdateDocumentInMemoryAsync(path, owned);
        var persisted = File.ReadAllBytes(path);
        Assert.Equal(owned, await PublishedSourceTextAsync(fixture, path));
        var external = owned.Replace("WriteLine(1)", "WriteLine(2)", StringComparison.Ordinal);
        File.WriteAllText(path, external);
        Notify(fixture, path, WatcherChangeTypes.Changed);
        await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.Equal(external, await PublishedSourceTextAsync(fixture, path));

        File.Delete(path);
        Notify(fixture, path, WatcherChangeTypes.Deleted);
        File.WriteAllBytes(path, persisted);
        Notify(fixture, path, WatcherChangeTypes.Created);
        await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();

        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.Equal(owned, await PublishedSourceTextAsync(fixture, path));
    }

    [Fact]
    public async Task Cancelled_disk_sync_keeps_the_restored_source_queued_for_the_next_TIA_request()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = fixture.Files.SpecsPath;
        var original = await PublishedSourceTextAsync(fixture, path);
        var restored = original.Replace("void Case() {}", "void Case() { System.Console.WriteLine(1); }", StringComparison.Ordinal);
        File.Delete(path);
        Notify(fixture, path, WatcherChangeTypes.Deleted);
        File.WriteAllText(path, restored);
        Notify(fixture, path, WatcherChangeTypes.Created);
        using var cancellation = new CancellationTokenSource();
        fixture.Manager.DuringDiskFlushForTests = cancellation.Cancel;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync(cancellation.Token));
        }
        finally
        {
            fixture.Manager.DuringDiskFlushForTests = null;
        }
        Assert.Equal(original, await PublishedSourceTextAsync(fixture, path));

        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.Contains("Changed since load: 1 members in 1 files", result, StringComparison.Ordinal);
        Assert.Equal(restored, await PublishedSourceTextAsync(fixture, path));
    }

    [Fact]
    public async Task Restored_source_stays_provisional_when_an_event_invalidates_each_read()
    {
        await using var fixture = new TestImpactWatcherFixture();
        await fixture.LoadAsync();
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = fixture.Files.SpecsPath;
        var original = File.ReadAllBytes(path);
        File.Delete(path);
        Notify(fixture, path, WatcherChangeTypes.Deleted);
        File.WriteAllBytes(path, original);
        Notify(fixture, path, WatcherChangeTypes.Created);
        var session = fixture.Manager.DiskWatcherSession!;
        fixture.Manager.DuringContentReadForTests = readPath => session.NotePendingContent(readPath);
        var tools = EstimateAffectedTestsTests.Tools(fixture.Manager);
        try
        {
            Assert.Contains("Project graph stale", await tools.EstimateAffectedTests(), StringComparison.Ordinal);
        }
        finally
        {
            fixture.Manager.DuringContentReadForTests = null;
        }

        AssertEmptyPlan(await tools.EstimateAffectedTests());
    }

    private const string Config = "root = true\n[*.cs]\nindent_size = 4\n";
    private const string ChangedConfig = "root = true\n[*.cs]\nindent_size = 2\n";

    private static string CreateConfig(TestImpactWatcherFixture fixture)
    {
        var path = Path.Combine(fixture.Files.Root, "Specs", ".editorconfig");
        File.WriteAllText(path, Config);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        return path;
    }

    private static void AssertConfigRole(TestImpactWatcherFixture fixture, string path)
    {
        var entry = fixture.Manager.DiskWatcherSession!.InputMap!.Find(path);
        Assert.NotNull(entry);
        Assert.NotEmpty(entry.Occurrences);
        Assert.All(entry.Occurrences, occurrence => Assert.Equal(InputRole.AnalyzerConfig, occurrence.Role));
    }

    private static void Notify(TestImpactWatcherFixture fixture, string path, WatcherChangeTypes changeType)
    {
        fixture.Manager.NotifyDiskWatcherChange(
            fixture.Manager.DiskWatcherSession!,
            new FileSystemEventArgs(changeType, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    private static async Task<string> PublishedSourceTextAsync(TestImpactWatcherFixture fixture, string path)
    {
        var solution = (await fixture.Manager.GetPublishedSolutionAsync())!;
        var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(path).Single())!;
        return (await document.GetTextAsync()).ToString();
    }

    private static async Task<string> PublishedConfigTextAsync(TestImpactWatcherFixture fixture, string path)
    {
        var solution = (await fixture.Manager.GetPublishedSolutionAsync())!;
        var document = solution.Projects.SelectMany(project => project.AnalyzerConfigDocuments)
            .First(document => InputPathCanon.Comparer.Equals(document.FilePath, path));
        return (await document.GetTextAsync()).ToString();
    }

    private static void AssertEmptyPlan(string result)
    {
        Assert.Contains("Changed since load: 0 members in 0 files", result, StringComparison.Ordinal);
        Assert.Contains("Run plan: 0 projects.", result, StringComparison.Ordinal);
        Assert.Contains("Not covered: none.", result, StringComparison.Ordinal);
    }
}
