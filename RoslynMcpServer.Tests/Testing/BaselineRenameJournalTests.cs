using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineRenameJournalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(bool commit)
    {
        await using var fixture = new BaselineLifecycleFixture();
        await Git(fixture.Root, "init --initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitignore"), "bin/\nobj/\n");
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        await Git(fixture.Root, "add -A");
        if (commit)
        {
            await Git(fixture.Root,
                "-c user.name=BaselineTests -c user.email=baseline@example.invalid -c commit.gpgsign=false commit -m baseline");
        }
        // Git objects are read-only on Windows; leave the temporary fixture removable after failed assertions too.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(fixture.Root, ".git", "objects"), "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        // Exercise the admitted rename deterministically as well as performing the real Git operation.
        var gitDirectory = Path.Combine(fixture.Root, ".git");
        fixture.Manager.NotifyDiskWatcherRename(fixture.Manager.DiskWatcherSession!,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, gitDirectory, "index", "index.lock"));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(view.TouchedPaths,
            path => WorkspaceDiskPathFilter.IsPathUnderDirectory(path, gitDirectory, InputPathCanon.Comparison));
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

    private static async Task Git(string directory, string arguments)
    {
        var result = await GitChangedFilesHelper.RunGitAsync(directory, arguments, CancellationToken.None);
        Assert.True(result.Success, result.Error ?? result.Output);
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
