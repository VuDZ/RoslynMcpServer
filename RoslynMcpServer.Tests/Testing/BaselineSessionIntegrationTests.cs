using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class BaselineSessionIntegrationTests
{
    [Fact]
    public async Task External_edit_before_first_read_is_unknown_and_immediately_touched()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        var initial = fixture.Manager.Baseline.GetSnapshot();
        Assert.Equal(2, initial.Documents.Length);
        await gate.Entered.WaitAsync(_timeout);
        await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { partial void Changed(); }");
        var delivery = await fixture.Manager.WaitForDirtySourceAsync(fixture.FirstPath, _timeout);
        Assert.True(delivery.Delivered);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
        Assert.Contains(fixture.FirstPath, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mcp_and_delivered_disk_edits_have_the_same_pending_baseline_outcome(bool mcp)
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        const string Updated = "partial class C { partial void Changed(); }";
        if (mcp)
        {
            await fixture.WriteThroughMcpAsync(fixture.FirstPath, Updated);
        }
        else
        {
            await File.WriteAllTextAsync(fixture.FirstPath, Updated);
            fixture.Deliver(fixture.FirstPath);
        }
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
        Assert.Contains(fixture.FirstPath, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
    }

    [Fact]
    public async Task Timestamp_guard_works_without_any_watcher_event()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        var initial = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { partial void Changed(); }");
        File.SetLastWriteTimeUtc(fixture.FirstPath, initial.PublishedAtUtc.AddMinutes(1));
        Assert.DoesNotContain(fixture.FirstPath, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
    }

    [Fact]
    public async Task Event_guard_works_when_the_original_mtime_is_preserved()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        var initial = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var mtime = File.GetLastWriteTimeUtc(fixture.FirstPath);
        await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { partial void Changed(); }");
        File.SetLastWriteTimeUtc(fixture.FirstPath, mtime);
        Assert.True(File.GetLastWriteTimeUtc(fixture.FirstPath) <= initial.PublishedAtUtc);
        fixture.Deliver(fixture.FirstPath);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
    }

    [Fact]
    public async Task Write_between_hashing_and_commit_cannot_publish_the_old_version()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeCommitForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var mtime = File.GetLastWriteTimeUtc(fixture.FirstPath);
        await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { partial void Changed(); }");
        File.SetLastWriteTimeUtc(fixture.FirstPath, mtime);
        fixture.Deliver(fixture.FirstPath);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var result = fixture.Manager.Baseline.GetSnapshot();
        AssertUnknown(result, fixture.FirstPath);
        Assert.Equal(BaselineDocumentStatus.Known, Find(result, fixture.SecondPath).Status);
    }

    [Fact]
    public async Task Partial_declarations_in_distinct_documents_keep_independent_member_snapshots()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var initial = fixture.Manager.Baseline.GetSnapshot();
        var first = Find(initial, fixture.FirstPath).Snapshot!;
        var second = Find(initial, fixture.SecondPath).Snapshot!;
        var firstMember = Assert.Single(first.Members.Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration));
        var secondMember = Assert.Single(second.Members.Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration));
        Assert.Equal(firstMember.Key, secondMember.Key);
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, "partial class C { [A] partial void Run(); }");
        await fixture.WriteThroughMcpAsync(fixture.SecondPath, "partial class C { partial void Run() { Work(); } }");
        var solution = await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        foreach (var path in new[] { fixture.FirstPath, fixture.SecondPath })
        {
            var document = Assert.Single(solution.Projects.SelectMany(project => project.Documents),
                document => InputPathCanon.Comparer.Equals(document.FilePath, path));
            var current = BaselineDocumentSnapshotBuilder.Build((await document.GetSyntaxTreeAsync())!);
            var baseline = Find(fixture.Manager.Baseline.GetSnapshot(), path).Snapshot!;
            var change = Assert.Single(BaselineDocumentChangeDetector.Compare(baseline, current));
            Assert.Equal(BaselineChangeKind.MemberModified, change.Kind);
            Assert.Equal(firstMember.Key, change.Key);
        }
        Assert.Same(first, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot);
        Assert.Same(second, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath).Snapshot);
    }

    [Fact]
    public async Task Csproj_reload_keeps_known_hashes_touched_paths_and_stable_document_keys()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var oldSolution = await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var initial = fixture.Manager.Baseline.GetSnapshot();
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, "partial class C { [A] partial void Run(); }");
        await ChangeProject(fixture);
        var reloaded = await fixture.LoadAsync();
        Assert.True(fixture.Manager.LastLoadReopenedGraph);
        Assert.NotEqual(oldSolution.ProjectIds[0], reloaded.ProjectIds[0]);
        var after = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(after.Generation > initial.Generation);
        Assert.Equal(initial.Documents.Select(document => document.Key), after.Documents.Select(document => document.Key));
        Assert.Contains(fixture.FirstPath, after.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(fixture.ProjectPath, after.TouchedPaths, InputPathCanon.Comparer);
        Assert.Same(Find(initial, fixture.FirstPath).Snapshot, Find(after, fixture.FirstPath).Snapshot);
    }

    [Fact]
    public async Task Same_solution_reopen_rejects_late_tail_but_preserves_already_published_hashes()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.SecondPath);
        fixture.Manager.Baseline.BeforeCommitForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        var oldTask = fixture.Manager.Baseline.CaptureTask;
        var initial = fixture.Manager.Baseline.GetSnapshot();
        Assert.Equal(BaselineDocumentStatus.Known, Find(initial, fixture.FirstPath).Status);
        await ChangeProject(fixture);
        await fixture.LoadAsync();
        Assert.Same(Find(initial, fixture.FirstPath).Snapshot, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath);
        gate.Release();
        await oldTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath);
    }

    [Fact]
    public async Task Cached_load_does_not_interrupt_an_unfinished_capture()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        var initial = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        await fixture.LoadAsync();
        Assert.True(fixture.Manager.LastLoadWasCacheHit);
        Assert.Equal(initial.Generation, fixture.Manager.Baseline.GetSnapshot().Generation);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        Assert.All(fixture.Manager.Baseline.GetSnapshot().Documents,
            document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    [Fact]
    public async Task Reset_returns_without_waiting_and_same_solution_reload_does_not_recapture_unknown_tail()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.SecondPath);
        fixture.Manager.Baseline.BeforeCommitForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        var initial = fixture.Manager.Baseline.GetSnapshot();
        var oldTask = fixture.Manager.Baseline.CaptureTask;
        await fixture.Manager.ClearWorkspaceAsync().WaitAsync(_timeout);
        Assert.False(oldTask.IsCompleted);
        Assert.Same(Find(initial, fixture.FirstPath).Snapshot, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath);
        gate.Release();
        await oldTask.WaitAsync(_timeout);
        await fixture.LoadAsync();
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath);
        Assert.Same(Find(initial, fixture.FirstPath).Snapshot, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot);
    }

    [Fact]
    public async Task Reset_before_first_read_freezes_the_published_inventory_as_unknown()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.FirstPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        Assert.Equal(2, fixture.Manager.Baseline.GetSnapshot().Documents.Length);
        await gate.Entered.WaitAsync(_timeout);
        var oldTask = fixture.Manager.Baseline.CaptureTask;
        await fixture.Manager.ClearWorkspaceAsync().WaitAsync(_timeout);
        Assert.All(fixture.Manager.Baseline.GetSnapshot().Documents,
            document => Assert.Equal(BaselineDocumentStatus.Unknown, document.Status));
        gate.Release();
        await oldTask.WaitAsync(_timeout);
        await fixture.LoadAsync();
        Assert.All(fixture.Manager.Baseline.GetSnapshot().Documents,
            document => Assert.Equal(BaselineDocumentStatus.Unknown, document.Status));
    }

    [Fact]
    public async Task Different_solution_discards_old_inventory_and_old_generation_completion()
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.SecondPath);
        fixture.Manager.Baseline.BeforeCommitForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await gate.Entered.WaitAsync(_timeout);
        var oldTask = fixture.Manager.Baseline.CaptureTask;
        var other = fixture.OtherProject();
        await fixture.Manager.LoadAsync(other);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var current = fixture.Manager.Baseline.GetSnapshot();
        Assert.Equal(other, current.WorkspacePath, InputPathCanon.Comparer);
        Assert.All(current.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
        Assert.DoesNotContain(current.Documents, document => InputPathCanon.Comparer.Equals(document.Key.ProjectPath, fixture.ProjectPath));
        gate.Release();
        await oldTask.WaitAsync(_timeout);
        Assert.Equal(current.Documents.ToArray(), fixture.Manager.Baseline.GetSnapshot().Documents.ToArray());
    }

    [Fact]
    public async Task Linked_document_contexts_are_not_collapsed_by_path_or_partial_member_key()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.Manager.LoadAsync(fixture.LinkedSolution());
        _ = fixture.Manager.Baseline.GetSnapshot();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var linked = fixture.Manager.Baseline.GetSnapshot().Documents
            .Where(document => InputPathCanon.Comparer.Equals(Path.GetFileName(document.Key.DocumentPath), "Shared.cs")).ToArray();
        Assert.Equal(2, linked.Length);
        Assert.NotEqual(linked[0].Key, linked[1].Key);
        var methods = linked.SelectMany(document => document.Snapshot!.Members)
            .Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration).Select(member => member.Key.Name).Order().ToArray();
        Assert.Equal(new[] { "First", "Second" }, methods);
    }

    [Fact]
    public async Task Opt_in_write_event_before_final_publication_is_not_accepted_as_baseline()
    {
        await using var fixture = new BaselineLifecycleFixture();
        fixture.Manager.AfterPhysicalLoadBeforePrepareAsync = async _ =>
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            var mtime = File.GetLastWriteTimeUtc(fixture.FirstPath);
            await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { [A] partial void Run(); }");
            File.SetLastWriteTimeUtc(fixture.FirstPath, mtime);
            fixture.Deliver(fixture.FirstPath);
        };
        await fixture.Manager.LoadAndPrepareAsync(fixture.ProjectPath, shadowCopyInSolutionAnalyzers: true, CancellationToken.None);
        _ = fixture.Manager.Baseline.GetSnapshot();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        AssertUnknown(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath);
        Assert.Contains(fixture.FirstPath, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
    }

    [Fact]
    public async Task External_edit_in_the_reset_watcher_gap_is_a_rescan_candidate_with_preserved_baseline()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        _ = fixture.Manager.Baseline.GetSnapshot();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var baseline = Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot!;
        await fixture.Manager.ClearWorkspaceAsync();
        await File.WriteAllTextAsync(fixture.FirstPath, "partial class C { [A] partial void Run(); }");
        var solution = await fixture.LoadAsync();
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.Contains(fixture.FirstPath, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Same(baseline, Find(view, fixture.FirstPath).Snapshot);
        var document = Assert.Single(solution.Projects.SelectMany(project => project.Documents),
            document => InputPathCanon.Comparer.Equals(document.FilePath, fixture.FirstPath));
        var current = BaselineDocumentSnapshotBuilder.Build((await document.GetSyntaxTreeAsync())!);
        Assert.Equal(BaselineChangeKind.MemberModified, Assert.Single(BaselineDocumentChangeDetector.Compare(baseline, current)).Kind);
    }

    [Fact]
    public async Task Linked_path_write_preserves_committed_context_but_invalidates_the_pending_context()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solutionPath = fixture.LinkedSolution();
        var sharedPath = Path.Combine(fixture.Root, "Shared.cs");
        using var gate = new BaselineCaptureGate(sharedPath);
        var contexts = 0;
        fixture.Manager.Baseline.BeforeCommitForTests = async (key, token) =>
        {
            if (InputPathCanon.Comparer.Equals(key.DocumentPath, sharedPath) && Interlocked.Increment(ref contexts) == 2)
            {
                await gate.WaitAsync(key, token);
            }
        };
        await fixture.Manager.LoadAsync(solutionPath);
        await gate.Entered.WaitAsync(_timeout);
        var before = fixture.Manager.Baseline.GetSnapshot().Documents
            .Where(document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, sharedPath)).ToArray();
        var known = Assert.Single(before, document => document.Status == BaselineDocumentStatus.Known);
        Assert.Single(before, document => document.Status == BaselineDocumentStatus.Pending);
        fixture.Manager.SilenceDiskWatchersForTests();
        var mtime = File.GetLastWriteTimeUtc(sharedPath);
        await File.WriteAllTextAsync(sharedPath, "class Shared { void Changed() {} }");
        File.SetLastWriteTimeUtc(sharedPath, mtime);
        fixture.Deliver(sharedPath);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var after = fixture.Manager.Baseline.GetSnapshot().Documents
            .Where(document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, sharedPath)).ToArray();
        Assert.Same(known.Snapshot, Assert.Single(after, document => document.Status == BaselineDocumentStatus.Known).Snapshot);
        Assert.Single(after, document => document.Status == BaselineDocumentStatus.Unknown);
    }

    [Fact]
    public async Task Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal()
    {
        await using var fixture = new BaselineLifecycleFixture();
        fixture.Manager.DiscardNextProvenanceSnapshot = true;
        await fixture.Manager.LoadAndPrepareAsync(fixture.ProjectPath, shadowCopyInSolutionAnalyzers: true, CancellationToken.None);
        Assert.Null(fixture.Manager.GetCurrentSolution());
        var snapshot = fixture.Manager.Baseline.GetSnapshot();
        Assert.Null(snapshot.WorkspacePath);
        Assert.Empty(snapshot.Documents);
        Assert.True(fixture.Manager.Baseline.CaptureTask.IsCompleted);
    }

    private static async Task ChangeProject(BaselineLifecycleFixture fixture)
    {
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        await fixture.WriteThroughMcpAsync(fixture.ProjectPath,
            project.Replace("<Nullable>enable</Nullable>", "<Nullable>disable</Nullable>", StringComparison.Ordinal));
    }

    private static BaselineDocumentState Find(BaselineSessionSnapshot snapshot, string path) =>
        Assert.Single(snapshot.Documents.Where(document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, path)));

    private static void AssertUnknown(BaselineSessionSnapshot snapshot, string path)
    {
        var document = Find(snapshot, path);
        Assert.Equal(BaselineDocumentStatus.Unknown, document.Status);
        Assert.Null(document.Snapshot);
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
