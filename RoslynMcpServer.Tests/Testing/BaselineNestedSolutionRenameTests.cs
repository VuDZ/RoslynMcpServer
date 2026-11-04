using System.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineNestedSolutionRenameTests
{
    [Theory]
    [InlineData("obj/foo", "obj/bar")]
    [InlineData("node_modules/.staging/pkg", "node_modules/.staging/pkg2")]
    public async Task Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(string oldName, string newName)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: false);
        var projectDirectory = Path.Combine(fixture.Root, "src", "App");
        var original = Path.GetFullPath(Path.Combine(projectDirectory, oldName));
        var renamed = Path.GetFullPath(Path.Combine(projectDirectory, newName));
        Directory.CreateDirectory(original);
        await File.WriteAllTextAsync(Path.Combine(original, "payload.txt"), "data");
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        Directory.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(renamed, InputPathCanon.Comparer)),
            "Native watcher did not admit the ignored directory rename.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.DoesNotContain(renamed, view.TouchedPaths, InputPathCanon.Comparer);
    }

    [Fact]
    public async Task Sibling_obj_file_native_rename_does_not_enter_journal()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: false);
        var directory = Path.Combine(fixture.Root, "src", "App", "obj");
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "temp.lock");
        var renamed = Path.Combine(directory, "index");
        await File.WriteAllTextAsync(original, "metadata");
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        File.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(renamed, InputPathCanon.Comparer)),
            "Native watcher did not admit the extensionless metadata rename.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.DoesNotContain(renamed, view.TouchedPaths, InputPathCanon.Comparer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sibling_git_add_and_commit_native_renames_do_not_enter_journal(bool commit)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: true);
        await Git(fixture.Root, "init --initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitignore"), "obj/\nbin/\n");
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        var gitDirectory = Path.Combine(fixture.Root, ".git");
        await Git(fixture.Root, "add -A");
        var index = Path.Combine(gitDirectory, "index");
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(index, InputPathCanon.Comparer)),
            "Native watcher did not admit Git's index rename.");
        if (commit)
        {
            await Git(fixture.Root,
                "-c user.name=BaselineTests -c user.email=baseline@example.invalid -c commit.gpgsign=false commit -m baseline");
            var head = Path.Combine(gitDirectory, "refs", "heads", "main");
            Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(head, InputPathCanon.Comparer)),
                "Native watcher did not admit Git's branch rename.");
        }
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(view.TouchedPaths,
            path => WorkspaceDiskPathFilter.IsPathUnderDirectory(path, gitDirectory, InputPathCanon.Comparison));
    }

    [Fact]
    public async Task Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: true);
        await Git(fixture.Root, "init --initial-branch=main");
        var original = Path.Combine(fixture.Root, ".git", "rebase-apply");
        var renamed = Path.Combine(fixture.Root, ".git", "rebase-merge");
        Directory.CreateDirectory(original);
        await File.WriteAllTextAsync(Path.Combine(original, "state"), "data");
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        // This is a real directory move inside .git, not a claim about a Git rebase operation.
        Directory.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(renamed, InputPathCanon.Comparer)),
            "Native watcher did not admit the .git directory move.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.DoesNotContain(renamed, view.TouchedPaths, InputPathCanon.Comparer);
    }

    [Fact]
    public async Task Sibling_source_native_rename_preserves_both_journal_paths()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: false);
        var directory = Path.Combine(fixture.Root, "src", "App");
        var original = Path.Combine(directory, "A.cs");
        var renamed = Path.Combine(directory, "C.cs");
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        AssertKnownAndUntouched(fixture, original);
        File.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.EventRevision(original) > 0 && session.EventRevision(renamed) > 0),
            "Native watcher did not admit both source rename endpoints.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(renamed, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    [Fact]
    public async Task Sibling_source_directory_native_rename_marks_gap()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: false);
        var directory = Path.Combine(fixture.Root, "src", "App");
        var original = Path.Combine(directory, "Sub");
        var renamed = Path.Combine(directory, "Sub2");
        Directory.CreateDirectory(original);
        File.Move(Path.Combine(directory, "A.cs"), Path.Combine(original, "A.cs"));
        var project = Path.Combine(directory, "App.csproj");
        var projectText = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project,
            projectText.Replace("Include=\"A.cs\"", "Include=\"Sub/A.cs\"", StringComparison.Ordinal));
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        AssertKnownAndUntouched(fixture, Path.Combine(original, "A.cs"));
        Directory.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.DirectoryRenamePaths.Contains(renamed, InputPathCanon.Comparer)),
            "Native watcher did not admit the source directory rename.");
        Assert.True(fixture.Manager.Baseline.GetSnapshot().RequiresFullScan);
    }

    [Fact]
    public async Task Sibling_explicit_obj_input_native_rename_preserves_both_journal_paths()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solution = CreateSolution(fixture, projectAtRoot: false);
        var projectDirectory = Path.Combine(fixture.Root, "src", "App");
        var directory = Path.Combine(projectDirectory, "obj");
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "Manual.cs");
        var renamed = Path.Combine(directory, "Moved.cs");
        await File.WriteAllTextAsync(original, "class Manual {}");
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(-1));
        var project = Path.Combine(projectDirectory, "App.csproj");
        var projectText = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project,
            projectText.Replace("<ItemGroup>", "<ItemGroup><Compile Include=\"obj/Manual.cs\" />", StringComparison.Ordinal));
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var session = fixture.Manager.DiskWatcherSession!;
        Assert.NotNull(session.InputMap!.Find(original));
        AssertKnownAndUntouched(fixture, original);
        File.Move(original, renamed);
        Assert.True(await WaitUntil(() => session.EventRevision(original) > 0 && session.EventRevision(renamed) > 0),
            "Native watcher did not admit the explicit input rename.");
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Contains(original, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(renamed, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.False(view.RequiresFullScan);
    }

    private static string CreateSolution(BaselineLifecycleFixture fixture, bool projectAtRoot)
    {
        var project = fixture.ProjectPath;
        if (!projectAtRoot)
        {
            var directory = Path.Combine(fixture.Root, "src", "App");
            Directory.CreateDirectory(directory);
            foreach (var name in new[] { "A.cs", "B.cs", "App.csproj" })
            {
                File.Copy(Path.Combine(fixture.Root, name), Path.Combine(directory, name));
            }
            project = Path.Combine(directory, "App.csproj");
        }
        var solutionDirectory = Path.Combine(fixture.Root, "build");
        Directory.CreateDirectory(solutionDirectory);
        var solution = Path.Combine(solutionDirectory, "All.slnx");
        var include = Path.GetRelativePath(solutionDirectory, project).Replace('\\', '/');
        File.WriteAllText(solution, $"<Solution><Project Path=\"{include}\" /></Solution>");
        return solution;
    }

    private static void AssertKnownAndUntouched(BaselineLifecycleFixture fixture, string path)
    {
        var view = fixture.Manager.Baseline.GetSnapshot();
        var document = Assert.Single(view.Documents,
            document => InputPathCanon.Comparer.Equals(document.Key.DocumentPath, path));
        Assert.Equal(BaselineDocumentStatus.Known, document.Status);
        Assert.DoesNotContain(path, view.TouchedPaths, InputPathCanon.Comparer);
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
        // Git objects are read-only on Windows; keep temporary fixture cleanup possible after assertions fail.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, ".git", "objects"), "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
}
