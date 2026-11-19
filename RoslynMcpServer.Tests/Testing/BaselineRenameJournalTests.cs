using System.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineRenameJournalTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "artifacts")]
    [InlineData(true, "artifacts")]
    [InlineData(false, "bin")]
    [InlineData(true, "bin")]
    public async Task Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(bool commit, string? ignoredAncestor)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = ignoredAncestor == null ? fixture.ProjectPath : WorkspaceUnder(fixture, ignoredAncestor);
        var directory = Path.GetDirectoryName(project)!;
        await Git(directory, "init --initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
        await fixture.Manager.LoadAsync(project);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        if (ignoredAncestor != null)
        {
            // Isolate rename admission: existing Changed/Created classification under such ancestors is outside this contract.
            fixture.Manager.SilenceDiskWatchersForTests();
        }
        await Git(directory, "add -A");
        if (commit)
        {
            await Git(directory,
                "-c user.name=BaselineTests -c user.email=baseline@example.invalid -c commit.gpgsign=false commit -m baseline");
        }
        // Git objects are read-only on Windows; leave the temporary fixture removable after failed assertions too.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, ".git", "objects"), "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        // Exercise the admitted rename deterministically as well as performing the real Git operation.
        var gitDirectory = Path.Combine(directory, ".git");
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, gitDirectory, "index", "index.lock"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(view.TouchedPaths,
            path => WorkspaceDiskPathFilter.IsPathUnderDirectory(path, gitDirectory, InputPathCanon.Comparison));
    }

    [Theory]
    [InlineData("artifacts")]
    [InlineData("bin")]
    public async Task Ignored_ancestor_source_rename_is_received_by_native_watcher_and_journaled(string ancestor)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = WorkspaceUnder(fixture, ancestor);
        var directory = Path.GetDirectoryName(project)!;
        var original = Path.Combine(directory, "A.cs");
        var renamed = Path.Combine(directory, "C.cs");
        await fixture.Manager.LoadAsync(project);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        AssertKnown(fixture.Manager.Baseline.GetSnapshot(), original);
        var before = session.EventRevision(original);
        File.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.EventRevision(original) > before && session.EventRevision(renamed) > 0),
            "Native watcher did not admit both sides of the source rename.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(renamed, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    [Theory]
    [InlineData("artifacts", "Sub")]
    [InlineData("bin", "Sub")]
    [InlineData("artifacts", "obj/Sub")]
    [InlineData("bin", "obj/Sub")]
    public async Task Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(string ancestor, string subdirectory)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = WorkspaceUnder(fixture, ancestor);
        var directory = Path.GetDirectoryName(project)!;
        var original = Path.GetFullPath(Path.Combine(directory, subdirectory));
        var renamed = original + "2";
        Directory.CreateDirectory(original);
        File.Move(Path.Combine(directory, "A.cs"), Path.Combine(original, "A.cs"));
        var projectText = await File.ReadAllTextAsync(project);
        var include = Path.GetRelativePath(directory, Path.Combine(original, "A.cs"));
        await File.WriteAllTextAsync(project,
            projectText.Replace("Include=\"A.cs\"", $"Include=\"{include}\"", StringComparison.Ordinal));
        await fixture.Manager.LoadAsync(project);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        AssertKnown(fixture.Manager.Baseline.GetSnapshot(), Path.Combine(original, "A.cs"));
        Directory.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(renamed, InputPathCanon.Comparer)),
            "Native watcher did not admit the directory rename.");
        Assert.True(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    [Theory]
    [InlineData(null, "rename")]
    [InlineData(null, "save")]
    [InlineData(null, "both")]
    [InlineData("artifacts", "rename")]
    [InlineData("artifacts", "save")]
    [InlineData("artifacts", "both")]
    [InlineData("bin", "rename")]
    [InlineData("bin", "save")]
    [InlineData("bin", "both")]
    public async Task Mapped_ignored_input_renames_preserve_both_endpoints(string? ancestor, string scenario)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = ancestor == null ? fixture.ProjectPath : WorkspaceUnder(fixture, ancestor);
        var directory = Path.Combine(Path.GetDirectoryName(project)!, "obj");
        Directory.CreateDirectory(directory);
        var manual = Path.Combine(directory, "Manual.cs");
        var moved = Path.Combine(directory, "Moved.cs");
        await File.WriteAllTextAsync(manual, "class Manual {}");
        File.SetLastWriteTimeUtc(manual, DateTime.UtcNow.AddDays(-1));
        var mapped = "<Compile Include=\"obj/Manual.cs\" />";
        if (scenario == "both")
        {
            await File.WriteAllTextAsync(moved, "class Moved {}");
            File.SetLastWriteTimeUtc(moved, DateTime.UtcNow.AddDays(-1));
            mapped += "<Compile Include=\"obj/Moved.cs\" />";
        }
        var projectText = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project,
            projectText.Replace("<ItemGroup>", "<ItemGroup>" + mapped, StringComparison.Ordinal));
        await fixture.Manager.LoadAsync(project);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var session = fixture.Manager.DiskWatcherSession!;
        Assert.NotNull(session.InputMap!.Find(manual));
        var original = scenario == "save" ? manual + ".tmp" : manual;
        var renamed = scenario == "save" ? manual : moved;
        var initial = fixture.Manager.Baseline.GetSnapshot();
        AssertKnown(initial, manual);
        Assert.DoesNotContain(original, initial.TouchedPaths, InputPathCanon.Comparer);
        Assert.DoesNotContain(renamed, initial.TouchedPaths, InputPathCanon.Comparer);
        if (scenario == "save")
        {
            await File.WriteAllTextAsync(original, "class Manual { int Updated; }");
            Assert.Null(session.InputMap.Find(original));
        }
        if (scenario == "both")
        {
            Assert.NotNull(session.InputMap.Find(renamed));
            AssertKnown(initial, renamed);
        }
        File.Move(original, renamed, overwrite: true);
        fixture.Manager.NotifyDiskWatcherRename(session,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, directory, Path.GetFileName(renamed), Path.GetFileName(original)));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(renamed, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    [Fact]
    public async Task Mapped_git_input_rename_preserves_the_unmapped_destination()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var directory = Path.Combine(fixture.Root, ".git");
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "Manual.cs");
        var renamed = Path.Combine(directory, "Moved.cs");
        await File.WriteAllTextAsync(original, "class Manual {}");
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(-1));
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        await File.WriteAllTextAsync(fixture.ProjectPath,
            project.Replace("<ItemGroup>", "<ItemGroup><Compile Include=\".git/Manual.cs\" />", StringComparison.Ordinal));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var initial = fixture.Manager.Baseline.GetSnapshot();
        AssertKnown(initial, original);
        Assert.DoesNotContain(original, initial.TouchedPaths, InputPathCanon.Comparer);
        Assert.DoesNotContain(renamed, initial.TouchedPaths, InputPathCanon.Comparer);
        File.Move(original, renamed);
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, directory, "Moved.cs", "Manual.cs"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(renamed, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("obj")]
    [InlineData("bin")]
    [InlineData("node_modules")]
    [InlineData("TestResults")]
    [InlineData("artifacts")]
    public async Task Ignored_file_rename_does_not_enter_baseline_journal(string directory)
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var parent = Path.Combine(fixture.Root, directory);
        Directory.CreateDirectory(parent);
        var before = Path.Combine(parent, "temp.lock");
        var after = Path.Combine(parent, "index");
        await File.WriteAllTextAsync(before, "metadata");
        File.Move(before, after);
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, parent, "index", "temp.lock"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(view.TouchedPaths, path => InputPathCanon.Comparer.Equals(path, before));
        Assert.DoesNotContain(view.TouchedPaths, path => InputPathCanon.Comparer.Equals(path, after));
    }

    [Fact]
    public async Task Extensionless_file_rename_is_an_ordinary_journal_entry_without_a_gap()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var before = Path.Combine(fixture.Root, "temp.lock");
        var after = Path.Combine(fixture.Root, "LICENSE");
        await File.WriteAllTextAsync(before, "license");
        File.Move(before, after);
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.Root, "LICENSE", "temp.lock"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.Contains(before, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(after, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.All(view.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership,
            fixture.Manager.DiskWatcherSession!.ObservationUnknownReasons);
    }

    [Fact]
    public async Task A_mapped_extensionless_file_does_not_supply_directory_evidence()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var path = Path.Combine(fixture.Root, "LICENSE");
        await File.WriteAllTextAsync(path, "class License {}");
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        await File.WriteAllTextAsync(fixture.ProjectPath,
            project.Replace("<ItemGroup>", "<ItemGroup><Compile Include=\"LICENSE\" />", StringComparison.Ordinal));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        Assert.NotNull(fixture.Manager.DiskWatcherSession!.InputMap!.Find(path));
        File.Move(path, Path.Combine(fixture.Root, "RENAMED"));
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.Root, "RENAMED", "LICENSE"));
        Assert.False(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    [Fact]
    public async Task A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var original = Path.Combine(fixture.Root, "Sub.Dir");
        var moved = Path.Combine(fixture.Root, "Moved.Dir");
        Directory.CreateDirectory(original);
        File.Move(fixture.FirstPath, Path.Combine(original, "A.cs"));
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        await File.WriteAllTextAsync(fixture.ProjectPath,
            project.Replace("Include=\"A.cs\"", "Include=\"Sub.Dir/A.cs\"", StringComparison.Ordinal));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        Directory.Move(original, moved);
        Directory.Delete(moved, recursive: true);
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.Root, "Moved.Dir", "Sub.Dir"));
        Assert.True(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    [Fact]
    public async Task Direct_project_rename_marks_baseline_gap_without_a_watcher_callback()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var project = fixture.OtherProject();
        await fixture.Manager.LoadAsync(project);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var original = fixture.Manager.Baseline.GetSnapshot().Documents;
        var plan = ProjectRenameHelper.CreatePlan(project, "Renamed", fixture.Root);
        ProjectRenameHelper.Apply(plan, fixture.Manager.DiskWatcherSession!, writeText: null);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.True(view.RequiresFullScan);
        Assert.True(File.Exists(plan.NewProjectPath));
        foreach (var document in original)
        {
            Assert.Same(document.Snapshot,
                Assert.Single(view.Documents, current => current.Key == document.Key).Snapshot);
            Assert.Contains(document.Key.DocumentPath, view.TouchedPaths, InputPathCanon.Comparer);
        }
    }

    private static void AssertKnown(BaselineSessionSnapshot snapshot, string path)
    {
        var document = Assert.Single(snapshot.Documents,
            document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, path));
        Assert.Equal(BaselineDocumentStatus.Known, document.Status);
    }

    private static string WorkspaceUnder(BaselineLifecycleFixture fixture, string ancestor)
    {
        var directory = Path.Combine(fixture.Root, ancestor, "Repo");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "A.cs", "B.cs", "App.csproj" })
        {
            File.Copy(Path.Combine(fixture.Root, name), Path.Combine(directory, name));
        }
        return Path.Combine(directory, "App.csproj");
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < _timeout)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(20);
        }
        return condition();
    }

    private static async Task Git(string directory, string arguments)
    {
        var result = await GitChangedFilesHelper.RunGitAsync(directory, arguments, CancellationToken.None);
        Assert.True(result.Success, result.Error ?? result.Output);
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
