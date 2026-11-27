using System.Reflection;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WpfTemporaryProjectCallbackTests
{
    [Theory]
    [InlineData(WatcherChangeTypes.Created, false)]
    [InlineData(WatcherChangeTypes.Changed, false)]
    [InlineData(WatcherChangeTypes.Deleted, false)]
    [InlineData(WatcherChangeTypes.Created, true)]
    [InlineData(WatcherChangeTypes.Changed, true)]
    [InlineData(WatcherChangeTypes.Deleted, true)]
    public void Temporary_events_use_loaded_membership_even_when_the_file_is_missing(
        WatcherChangeTypes changeType, bool loaded)
    {
        var manager = SolutionManagerTestFactory.Create();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Loaded_wpftmp.csproj");
        var session = manager.StartDiskWatcherSessionForTests(loaded ? [path] : []);
        Assert.False(File.Exists(path));

        manager.NotifyDiskWatcherChange(session, Change(path, changeType));

        Assert.Equal(loaded, manager.ProjectGraphStale);
        Assert.False(manager.RefreshAllDocumentsPending);
        Assert.Empty(manager.GetPendingDirtySourcePaths());
    }

    [Theory]
    [InlineData("Real.csproj", "Other_wpftmp.csproj", null, true)]
    [InlineData("Other_wpftmp.csproj", "Real.csproj", null, true)]
    [InlineData("Loaded_wpftmp.csproj", "Other_wpftmp.csproj", "Loaded_wpftmp.csproj", true)]
    [InlineData("Other_wpftmp.csproj", "Loaded_wpftmp.csproj", "Loaded_wpftmp.csproj", true)]
    [InlineData("Other_wpftmp.csproj", "Next_wpftmp.csproj", null, false)]
    public void Rename_checks_both_sides_independently(string oldName, string newName, string? loadedName, bool stale)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var manager = SolutionManagerTestFactory.Create();
        var session = manager.StartDiskWatcherSessionForTests(loadedName is null ? [] : [Path.Combine(directory, loadedName)]);

        manager.NotifyDiskWatcherRename(session,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, directory, newName, oldName));

        Assert.Equal(stale, manager.ProjectGraphStale);
        Assert.False(manager.RefreshAllDocumentsPending);
    }

    [Theory]
    [InlineData("Real.csproj")]
    [InlineData("Real.sln")]
    [InlineData("Real.slnx")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData("Foo_wpftmp_Bar.csproj")]
    public void Real_graph_files_keep_their_existing_invalidation(string name)
    {
        var manager = SolutionManagerTestFactory.Create();
        var session = manager.StartDiskWatcherSessionForTests([]);
        manager.NotifyDiskWatcherChange(session, Change(Path.Combine(Path.GetTempPath(), name)));
        Assert.True(manager.ProjectGraphStale);
        Assert.Contains("changed on disk", manager.GetProjectGraphStaleHint());
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("TestResults")]
    [InlineData("artifacts")]
    public void Ignored_trees_stay_ignored_even_for_loaded_project_candidates(string directory)
    {
        var manager = SolutionManagerTestFactory.Create();
        var path = Path.Combine(Path.GetTempPath(), directory, "Loaded_wpftmp.csproj");
        var session = manager.StartDiskWatcherSessionForTests([path]);
        manager.NotifyDiskWatcherChange(session, Change(path));
        Assert.False(manager.ProjectGraphStale);
    }

    [Fact]
    public void Snapshot_normalizes_paths_and_uses_the_operating_system_comparer()
    {
        var manager = SolutionManagerTestFactory.Create();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "Loaded_wpftmp.csproj");
        var paths = new List<string?> { Path.Combine(directory, "nested", "..", "Loaded_wpftmp.csproj"), null, " " };
        var session = manager.StartDiskWatcherSessionForTests(paths);
        paths.Clear();
        Assert.Equal([path], session.LoadedProjectPaths);
        var changedCase = Path.Combine(directory, "LOADED_WPFTMP.CSPROJ");
        manager.NotifyDiskWatcherChange(session, Change(changedCase));
        Assert.Equal(OperatingSystem.IsWindows(), manager.ProjectGraphStale);
    }

    [Fact]
    public void Known_source_files_and_self_write_suppression_keep_their_behavior()
    {
        var manager = SolutionManagerTestFactory.Create();
        var session = manager.StartDiskWatcherSessionForTests([]);
        var source = Path.Combine(Path.GetTempPath(), "Known.cs");
        var suppressed = Path.Combine(Path.GetTempPath(), "Saved.cs");
        manager.SuppressDiskWatchForPath(suppressed);
        manager.NotifyDiskWatcherChange(session, Change(suppressed));
        manager.NotifyDiskWatcherChange(session, Change(source));
        Assert.Equal([source], manager.GetPendingDirtySourcePaths());
        Assert.False(manager.ProjectGraphStale);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("source")]
    [InlineData("rename")]
    [InlineData("directory")]
    [InlineData("error")]
    public async Task Callback_paused_before_admission_cannot_write_after_reset_and_session_replacement(string kind)
    {
        var manager = SolutionManagerTestFactory.Create();
        var path = Path.Combine(Path.GetTempPath(), "Loaded_wpftmp.csproj");
        var oldSession = manager.StartDiskWatcherSessionForTests([path]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        manager.BeforeDiskWatcherCallbackForTests = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "The callback must be released.");
        };
        var callback = Task.Run(() => Deliver(manager, oldSession, path, kind));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The callback must reach admission.");
            await manager.ClearWorkspaceAsync();
            var newSession = manager.StartDiskWatcherSessionForTests([path]);
            Assert.NotEqual(oldSession.Generation, newSession.Generation);
            release.Set();
            await callback.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(manager.ProjectGraphStale);
            Assert.False(manager.RefreshAllDocumentsPending);
            Assert.Empty(manager.GetPendingDirtySourcePaths());
        }
        finally
        {
            release.Set();
            await callback;
        }
    }

    [Theory]
    [InlineData("change")]
    [InlineData("source")]
    [InlineData("rename")]
    [InlineData("directory")]
    [InlineData("error")]
    public async Task Callbacks_do_not_acquire_the_workspace_semaphore(string kind)
    {
        var manager = SolutionManagerTestFactory.Create();
        var path = Path.Combine(Path.GetTempPath(), "Loaded_wpftmp.csproj");
        var session = manager.StartDiskWatcherSessionForTests([path]);
        var semaphore = (SemaphoreSlim)typeof(SolutionManager)
            .GetField("_workspaceLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        await semaphore.WaitAsync();
        var callback = Task.Run(() => Deliver(manager, session, path, kind));
        try
        {
            await callback.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            semaphore.Release();
            await callback;
        }
    }

    [Fact]
    public async Task Closing_a_session_waits_for_an_admitted_mutation_and_rejects_later_updates()
    {
        var session = new WorkspaceInputSession([]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var mutations = 0;
        var update = Task.Run(() => session.TryRun(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "The admitted mutation must be released.");
            mutations++;
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The mutation must enter the session gate.");
        var close = Task.Run(session.Close);
        try
        {
            Assert.False(close.Wait(TimeSpan.FromMilliseconds(100)), "Closure must wait for the admitted mutation.");
        }
        finally
        {
            release.Set();
            await Task.WhenAll(update, close).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(1, mutations);
        Assert.False(session.TryRun(() => mutations++));
        Assert.Equal(1, mutations);
    }

    private static FileSystemEventArgs Change(string path, WatcherChangeTypes changeType = WatcherChangeTypes.Changed)
        => new(changeType, Path.GetDirectoryName(path)!, Path.GetFileName(path));

    private static void Deliver(SolutionManager manager, WorkspaceInputSession session, string path, string kind)
    {
        switch (kind)
        {
            case "change":
                manager.NotifyDiskWatcherChange(session, Change(path));
                break;
            case "source":
                manager.NotifyDiskWatcherChange(session, Change(Path.ChangeExtension(path, ".cs")));
                break;
            case "rename":
                manager.NotifyDiskWatcherRename(session,
                    new RenamedEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(path)!,
                        "Other_wpftmp.csproj", Path.GetFileName(path)));
                break;
            case "directory":
                manager.NotifyDiskWatcherRename(session,
                    new RenamedEventArgs(WatcherChangeTypes.Renamed, Path.GetTempPath(), null, null));
                break;
            case "error":
                manager.NotifyDiskWatcherError(session, new InternalBufferOverflowException());
                break;
            default:
                throw new ArgumentException("Unknown callback kind.", nameof(kind));
        }
    }
}
