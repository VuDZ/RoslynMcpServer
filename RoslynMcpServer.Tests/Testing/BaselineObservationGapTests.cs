using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineObservationGapTests
{
    [Theory]
    [InlineData("overflow")]
    [InlineData("error")]
    [InlineData("coverage")]
    [InlineData("directory")]
    [InlineData("outside-directory")]
    public async Task Observation_gap_freezes_pending_documents_and_retains_known_hashes(string gap)
    {
        await using var fixture = new BaselineLifecycleFixture();
        using var gate = new BaselineCaptureGate(fixture.SecondPath);
        fixture.Manager.Baseline.BeforeReadForTests = gate.WaitAsync;
        await fixture.LoadAsync();
        await gate.Entered.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var known = Find(fixture.Manager.Baseline.GetSnapshot(), fixture.FirstPath).Snapshot;
        Assert.NotNull(known);
        var session = fixture.Manager.DiskWatcherSession!;
        SignalGap(fixture, session, gap);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.Same(known, Find(view, fixture.FirstPath).Snapshot);
        Assert.Equal(BaselineDocumentStatus.Unknown, Find(view, fixture.SecondPath).Status);
        Assert.Contains(fixture.FirstPath, view.TouchedPaths, InputPathCanon.Comparer);
        gate.Release();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        Assert.Equal(BaselineDocumentStatus.Unknown, Find(fixture.Manager.Baseline.GetSnapshot(), fixture.SecondPath).Status);
        var generation = view.Generation;
        await fixture.LoadAsync();
        Assert.Equal(generation, fixture.Manager.Baseline.GetSnapshot().Generation);
        Assert.True(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_gap_survives_first_publication(bool missingDirectory)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = fixture.OtherProject();
        var external = Path.Combine(fixture.Root, "External");
        Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(Path.Combine(external, "input.txt"), "input");
        var projectText = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project, projectText.Replace("</Project>",
            "<ItemGroup><AdditionalFiles Include=\"../External/input.txt\" /></ItemGroup></Project>", StringComparison.Ordinal));
        fixture.Manager.BeforeDiskWatcherStartForTests = () =>
        {
            if (missingDirectory)
            {
                // Remove an external point-watch directory; C# baseline files stay available throughout capture.
                Directory.Delete(external, recursive: true);
            }
            else
            {
                WorkspaceDiskWatcherStarter.FailNextStartForTests = true;
            }
        };
        try
        {
            await fixture.Manager.LoadAsync(project);
        }
        finally
        {
            WorkspaceDiskWatcherStarter.FailNextStartForTests = false;
        }
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        Assert.Contains(missingDirectory ? InputCoverageReason.WatcherDirectoryMissing : InputCoverageReason.WatcherStartFailed,
            fixture.Manager.DiskWatcherSession!.ObservationUnknownReasons);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.All(view.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    [Fact]
    public async Task Normal_publication_waits_until_watcher_startup_finishes()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        fixture.Manager.BeforeDiskWatcherStartForTests = () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(_timeout));
        };
        BaselineSessionSnapshot? afterStartup = null;
        fixture.Manager.AfterDiskWatcherStartForTests = () => afterStartup = fixture.Manager.Baseline.GetSnapshot();
        var loading = Task.Run(fixture.LoadAsync);
        BaselineSessionSnapshot beforeStartup;
        try
        {
            await entered.Task.WaitAsync(_timeout);
            beforeStartup = fixture.Manager.Baseline.GetSnapshot();
        }
        finally
        {
            release.Set();
        }
        await loading;
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        Assert.Null(beforeStartup.WorkspacePath);
        Assert.Empty(beforeStartup.Documents);
        Assert.Equal(default(DateTime), beforeStartup.PublishedAtUtc);
        // UTC can move backward; hook snapshots prove publication order without comparing wall-clock readings.
        // See docs/rapid-tia/epoch-1-project-level/task-03-implementation-report.md (R7-001).
        Assert.NotNull(afterStartup);
        Assert.Null(afterStartup.WorkspacePath);
        Assert.Empty(afterStartup.Documents);
        Assert.Equal(default(DateTime), afterStartup.PublishedAtUtc);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.NotNull(view.WorkspacePath);
        Assert.NotEqual(default(DateTime), view.PublishedAtUtc);
        Assert.Equal(2, view.Documents.Length);
        Assert.All(view.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    [Fact]
    public async Task Event_admitted_after_startup_is_journaled_before_publication()
    {
        await using var fixture = new BaselineLifecycleFixture();
        fixture.Manager.AfterDiskWatcherStartForTests = () =>
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            var mtime = File.GetLastWriteTimeUtc(fixture.FirstPath);
            File.WriteAllText(fixture.FirstPath, "partial class C { partial void Changed(); }");
            File.SetLastWriteTimeUtc(fixture.FirstPath, mtime);
            fixture.Deliver(fixture.FirstPath);
        };
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Equal(BaselineDocumentStatus.Unknown, Find(view, fixture.FirstPath).Status);
        Assert.Contains(fixture.FirstPath, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    [Fact]
    public async Task Opt_in_prepublication_gap_is_persistent()
    {
        await using var fixture = new BaselineLifecycleFixture();
        fixture.Manager.AfterPhysicalLoadBeforePrepareAsync = _ =>
        {
            fixture.Manager.DiskWatcherSession!.NoteCoverageUnknown(InputCoverageReason.WatcherBufferOverflow);
            return Task.CompletedTask;
        };
        await fixture.Manager.LoadAndPrepareAsync(fixture.ProjectPath, shadowCopyInSolutionAnalyzers: true, CancellationToken.None);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.All(view.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    [Fact]
    public async Task Prepublication_gap_still_rejects_an_already_journaled_write()
    {
        await using var fixture = new BaselineLifecycleFixture();
        fixture.Manager.AfterDiskWatcherStartForTests = () =>
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            fixture.Manager.DiskWatcherSession!.NoteCoverageUnknown(InputCoverageReason.WatcherBufferOverflow);
            fixture.Deliver(fixture.FirstPath);
        };
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.Equal(BaselineDocumentStatus.Unknown, Find(view, fixture.FirstPath).Status);
        Assert.Equal(BaselineDocumentStatus.Known, Find(view, fixture.SecondPath).Status);
    }

    [Fact]
    public async Task Actual_directory_move_retains_known_source_as_a_rescan_candidate()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var oldDirectory = Path.Combine(fixture.Root, "Sub");
        var newDirectory = Path.Combine(fixture.Root, "Moved");
        Directory.CreateDirectory(oldDirectory);
        var original = Path.Combine(oldDirectory, "A.cs");
        File.Move(fixture.FirstPath, original);
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        await File.WriteAllTextAsync(fixture.ProjectPath, project.Replace("Include=\"A.cs\"", "Include=\"Sub/A.cs\"", StringComparison.Ordinal));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var known = Find(fixture.Manager.Baseline.GetSnapshot(), original).Snapshot;
        Directory.Move(oldDirectory, newDirectory);
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.Root, "Moved", "Sub"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Same(known, Find(view, original).Snapshot);
    }

    [Fact]
    public async Task Closed_session_gaps_cannot_affect_a_different_identity()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var oldSession = fixture.Manager.DiskWatcherSession!;
        oldSession.NoteCoverageUnknown(InputCoverageReason.WatcherBufferOverflow);
        await fixture.Manager.LoadAsync(fixture.OtherProject());
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var before = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(before.RequiresFullScan);
        oldSession.NoteCoverageUnknown(InputCoverageReason.WatcherStartFailed);
        oldSession.NoteDirectoryRename(fixture.Root, fixture.Root + "-old", affectsMembership: true);
        fixture.Manager.NotifyDiskWatcherError(oldSession, new IOException("Stale watcher."));
        var after = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(after.RequiresFullScan);
        Assert.Equal(before.Generation, after.Generation);
        Assert.All(after.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    [Fact]
    public async Task Ordinary_write_does_not_request_a_full_scan()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        await fixture.WriteThroughMcpAsync(fixture.FirstPath, "partial class C { partial void Changed(); }");
        fixture.Deliver(fixture.FirstPath);
        Assert.False(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    private static void SignalGap(BaselineLifecycleFixture fixture, WorkspaceInputSession session, string gap)
    {
        switch (gap)
        {
            case "overflow":
                fixture.Manager.NotifyDiskWatcherError(session, new InternalBufferOverflowException("Watcher overflow."));
                break;
            case "error":
                fixture.Manager.NotifyDiskWatcherError(session, new IOException("Watcher failure."));
                break;
            case "coverage":
                session.NoteCoverageUnknown(InputCoverageReason.WatcherDirectoryMissing);
                break;
            default:
                session.NoteDirectoryRename(fixture.Root, fixture.Root + "-renamed", affectsMembership: gap == "directory");
                break;
        }
    }

    private static BaselineDocumentState Find(BaselineSessionSnapshot snapshot, string path) =>
        Assert.Single(snapshot.Documents, document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, path));

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
