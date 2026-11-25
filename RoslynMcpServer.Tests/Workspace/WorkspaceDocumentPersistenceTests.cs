using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.Models;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceDocumentPersistenceTests(ITestOutputHelper output) : IDisposable
{
    [Fact]
    public async Task Persisting_one_edit_avoids_reading_untouched_documents()
    {
        const int UntouchedCount = 96;
        Directory.CreateDirectory(_root);
        var untouchedPaths = Enumerable.Range(0, UntouchedCount)
            .Select(index => Path.Combine(_root, $"Untouched{index}.cs"))
            .ToArray();
        var source = new string('x', 4096);
        foreach (var path in untouchedPaths)
        {
            await File.WriteAllTextAsync(path, source);
        }

        var editedPath = Path.Combine(_root, "Edited.cs");
        var manager = SolutionManagerTestFactory.Create();
        var persist = BindPersistence(manager);
        for (var run = 0; run <= 5; run++)
        {
            await File.WriteAllTextAsync(editedPath, source);
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var before = workspace.CurrentSolution.AddProject(projectId, "App", "App", LanguageNames.CSharp);
            var reads = new ReadCounter();
            foreach (var path in untouchedPaths)
            {
                before = before.AddDocument(CreateDocument(DocumentId.CreateNewId(projectId), path, reads));
            }

            var editedId = DocumentId.CreateNewId(projectId);
            before = before.AddDocument(CreateDocument(editedId, editedPath, new ReadCounter()));
            var after = before.WithDocumentText(editedId, SourceText.From("class Edited {}"));
            var saved = new List<string>();
            var timestamp = Stopwatch.GetTimestamp();
            await persist(before, after, saved, [], checkedPatchSource: null, CancellationToken.None);
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            Assert.Equal(editedPath, Assert.Single(saved));
            Assert.Equal(0, reads.Loads);
            Assert.Equal(0, reads.Bytes);
            if (run > 0)
            {
                output.WriteLine(
                    "Run {0}: {1} untouched text loads, {2} bytes, {3:F2} ms",
                    run,
                    reads.Loads,
                    reads.Bytes,
                    elapsed.TotalMilliseconds);
            }
        }
    }

    [Fact]
    public async Task Writing_linked_memberships_and_an_added_document_persists_each_path_once()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await manager.LoadAsync(fixture.LinkedSolution());
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var sharedPath = Path.Combine(fixture.Root, "Shared.cs");
        var sharedIds = before.GetDocumentIdsWithFilePath(sharedPath);
        Assert.Equal(2, sharedIds.Length);

        const string SharedEdit = "class Shared { public int Value; }";
        const string AddedText = "class Added {}";
        var sharedText = SourceText.From(SharedEdit);
        var after = before;
        foreach (var sharedId in sharedIds)
        {
            after = after.WithDocumentText(sharedId, sharedText);
        }

        var addedProject = before.GetProject(sharedIds[0].ProjectId)!;
        var addedPath = Path.Combine(Path.GetDirectoryName(addedProject.FilePath)!, "Added.cs");
        var addedId = DocumentId.CreateNewId(addedProject.Id);
        after = after.AddDocument(addedId, "Added.cs", SourceText.From(AddedText), filePath: addedPath);
        var commitTimestamp = new DateTime(2005, 4, 3, 2, 1, 0, DateTimeKind.Utc);
        manager.AfterDocumentCommitForTests = path => File.SetLastWriteTimeUtc(path, commitTimestamp);
        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
        Assert.Equal(2, result.SavedPaths.Count);
        Assert.Contains(sharedPath, result.SavedPaths);
        Assert.Contains(addedPath, result.SavedPaths);
        Assert.Equal(SharedEdit, await File.ReadAllTextAsync(sharedPath));
        Assert.Equal(commitTimestamp, File.GetLastWriteTimeUtc(sharedPath));
        Assert.Equal(AddedText, await File.ReadAllTextAsync(addedPath));
        Assert.Contains("Include=\"Added.cs\"", await File.ReadAllTextAsync(addedProject.FilePath!), StringComparison.Ordinal);
        var published = (await manager.GetPublishedSolutionAsync())!;
        foreach (var sharedId in sharedIds)
        {
            Assert.Equal(SharedEdit, (await published.GetDocument(sharedId)!.GetTextAsync()).ToString());
        }

        Assert.Equal(AddedText, (await published.GetDocument(addedId)!.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_write_reconciles_only_the_saved_path(bool cancel)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var documents = before.Projects.Single().Documents.ToArray();
        Assert.Equal(2, documents.Length);
        var first = documents[0];
        var second = documents[1];
        var originalSecond = await File.ReadAllTextAsync(second.FilePath!);
        const string FirstEdit = "class FirstEdit {}";
        var after = before.WithDocumentText(first.Id, SourceText.From(FirstEdit))
            .WithDocumentText(second.Id, SourceText.From("class SecondEdit {}"));
        if (cancel)
        {
            manager.CancelAfterDocumentWrites = 1;
        }
        else
        {
            manager.FailNextDocumentWritePath = second.FilePath;
        }

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.Equal(WorkspaceWriteStatus.ReconciliationSucceeded, result.Status);
        Assert.StartsWith(cancel ? "cancelled" : "IOException: injected-file-write-failure:", result.Reason);
        Assert.Equal(first.FilePath, Assert.Single(result.SavedPaths));
        Assert.Equal(FirstEdit, await File.ReadAllTextAsync(first.FilePath!));
        Assert.Equal(originalSecond, await File.ReadAllTextAsync(second.FilePath!));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(FirstEdit, (await published.GetDocument(first.Id)!.GetTextAsync()).ToString());
        Assert.Equal(originalSecond, (await published.GetDocument(second.Id)!.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_staging_preserves_old_bytes_and_publishes_prior_commit(bool cancel)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var documents = before.Projects.Single().Documents.ToArray();
        var first = documents[0];
        var second = documents[1];
        var originalSecond = await File.ReadAllBytesAsync(second.FilePath!);
        const string FirstEdit = "class FirstEdit {}";
        var after = before.WithDocumentText(first.Id, SourceText.From(FirstEdit))
            .WithDocumentText(second.Id, SourceText.From("class SecondEdit {}"));
        using var cancellation = new CancellationTokenSource();
        manager.AfterStagingPrefixForTests = path =>
        {
            if (path != second.FilePath)
            {
                return;
            }

            if (cancel)
            {
                cancellation.Cancel();
                return;
            }

            throw new IOException("Failed after writing a staged prefix.");
        };

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, cancellation.Token);

        Assert.Equal(WorkspaceWriteStatus.ReconciliationSucceeded, result.Status);
        Assert.StartsWith(cancel ? "cancelled" : "IOException:", result.Reason);
        Assert.Equal(first.FilePath, Assert.Single(result.SavedPaths));
        Assert.Equal(FirstEdit, await File.ReadAllTextAsync(first.FilePath!));
        Assert.Equal(originalSecond, await File.ReadAllBytesAsync(second.FilePath!));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(FirstEdit, (await published.GetDocument(first.Id)!.GetTextAsync()).ToString());
        Assert.Equal(
            (await before.GetDocument(second.Id)!.GetTextAsync()).ToString(),
            (await published.GetDocument(second.Id)!.GetTextAsync()).ToString());
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, ".roslyn-mcp-write-*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Failure_after_the_commit_keeps_the_confirmed_path_and_snapshot()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var first = before.Projects.Single().Documents.First();
        const string FirstEdit = "class FirstEdit {}";
        var after = before.WithDocumentText(first.Id, SourceText.From(FirstEdit));
        manager.AfterDocumentCommitForTests = _ => throw new IOException("Failed after commit.");

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.Equal(WorkspaceWriteStatus.ReconciliationSucceeded, result.Status);
        Assert.Equal(first.FilePath, Assert.Single(result.SavedPaths));
        Assert.Equal(FirstEdit, await File.ReadAllTextAsync(first.FilePath!));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(FirstEdit, (await published.GetDocument(first.Id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task Added_document_cannot_replace_an_existing_file_before_residual_apply()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var project = before.Projects.Single();
        var document = project.Documents.First();
        var original = await File.ReadAllBytesAsync(document.FilePath!);
        var sourcePath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "Existing.cs");
        const string ExistingText = "preserve";
        await File.WriteAllTextAsync(sourcePath, ExistingText);
        var addedId = DocumentId.CreateNewId(project.Id);
        var after = before.WithDocumentText(document.Id, SourceText.From("class Edited {}"))
            .AddDocument(addedId, "Existing.cs", SourceText.From("class Replacement {}"), filePath: sourcePath);

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.False(result.IsFullSuccess);
        Assert.Empty(result.SavedPaths);
        Assert.Equal(original, await File.ReadAllBytesAsync(document.FilePath!));
        Assert.Equal(ExistingText, await File.ReadAllTextAsync(sourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_staging_or_colliding_creation_does_not_register_a_new_document(bool collision)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var project = before.Projects.Single();
        var projectPath = project.FilePath!;
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var existing = project.Documents.First();
        var addedPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "Added.cs");
        var addedId = DocumentId.CreateNewId(project.Id);
        const string Edit = "class Edited {}";
        const string External = "external";
        var after = before.WithDocumentText(existing.Id, SourceText.From(Edit))
            .AddDocument(addedId, "Added.cs", SourceText.From("class Added {}"), filePath: addedPath);
        manager.AfterStagingPrefixForTests = path =>
        {
            if (path != addedPath)
            {
                return;
            }

            if (collision)
            {
                File.WriteAllText(addedPath, External);
                return;
            }

            throw new IOException("Stopped after writing only a staged prefix.");
        };

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.False(result.IsFullSuccess);
        Assert.Equal(existing.FilePath, Assert.Single(result.SavedPaths));
        if (collision)
        {
            Assert.Equal(External, await File.ReadAllTextAsync(addedPath));
        }
        else
        {
            Assert.False(File.Exists(addedPath));
        }

        Assert.Equal(projectBytes, await File.ReadAllBytesAsync(projectPath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Null(published.GetDocument(addedId));
        Assert.Equal(Edit, (await published.GetDocument(existing.Id)!.GetTextAsync()).ToString());
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, ".roslyn-mcp-write-*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Two_additions_through_physical_directory_aliases_are_rejected_before_writing()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var project = before.Projects.Single();
        var directory = Path.GetDirectoryName(project.FilePath)!;
        var actual = Path.Combine(directory, "actual");
        var alias = Path.Combine(directory, "alias");
        Directory.CreateDirectory(actual);
        await CreateDirectoryAliasAsync(alias, actual);
        try
        {
            const string Name = "Added.cs";
            var actualPath = Path.Combine(actual, Name);
            var aliasPath = Path.Combine(alias, Name);
            var after = before.AddDocument(DocumentId.CreateNewId(project.Id), Name,
                    SourceText.From("class First {}"), folders: ["actual"], filePath: actualPath)
                .AddDocument(DocumentId.CreateNewId(project.Id), Name,
                    SourceText.From("class Second {}"), folders: ["alias"], filePath: aliasPath);

            var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

            Assert.False(result.IsFullSuccess);
            Assert.Empty(result.SavedPaths);
            Assert.False(File.Exists(actualPath));
            Assert.False(File.Exists(aliasPath));
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_a_document_through_a_directory_alias_does_not_delete_the_edited_file(bool nested)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var projectPath = fixture.ProjectPath;
        var directory = Path.GetDirectoryName(projectPath)!;
        var actualRoot = Path.Combine(directory, "actual");
        var actual = nested ? Path.Combine(actualRoot, "nested") : actualRoot;
        var alias = Path.Combine(directory, "alias");
        var intermediate = Path.Combine(directory, "intermediate");
        Directory.CreateDirectory(actual);
        try
        {
            if (nested)
            {
                await CreateDirectoryAliasAsync(intermediate, actualRoot);
                await CreateDirectoryAliasAsync(alias, Path.Combine(intermediate, "nested"));
                Assert.Contains(
                    $"{Path.DirectorySeparatorChar}intermediate{Path.DirectorySeparatorChar}",
                    new DirectoryInfo(alias).LinkTarget!,
                    StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                await CreateDirectoryAliasAsync(alias, actual);
            }

            const string Source = "class Original {}";
            var actualPath = Path.Combine(actual, "Shared.cs");
            var aliasPath = Path.Combine(alias, "Shared.cs");
            await File.WriteAllTextAsync(actualPath, Source);
            var projectFile = XDocument.Load(projectPath);
            projectFile.Root!.Add(new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", Path.GetRelativePath(directory, actualPath))),
                new XElement("Compile", new XAttribute("Include", Path.GetRelativePath(directory, aliasPath)))));
            projectFile.Save(projectPath);
            var projectBytes = await File.ReadAllBytesAsync(projectPath);

            var manager = fixture.Manager;
            await fixture.LoadAsync();
            manager.SilenceDiskWatchersForTests();
            var before = (await manager.GetPublishedSolutionAsync())!;
            var edited = Assert.Single(before.GetDocumentIdsWithFilePath(actualPath));
            var removed = Assert.Single(before.GetDocumentIdsWithFilePath(aliasPath));
            var after = before.WithDocumentText(edited, SourceText.From("class Edited {}")).RemoveDocument(removed);

            var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

            Assert.False(result.IsFullSuccess);
            Assert.Contains("cannot remove a document written by this edit", result.Reason, StringComparison.Ordinal);
            Assert.Empty(result.SavedPaths);
            Assert.Equal(Source, await File.ReadAllTextAsync(actualPath));
            Assert.Equal(Source, await File.ReadAllTextAsync(aliasPath));
            Assert.Equal(projectBytes, await File.ReadAllBytesAsync(projectPath));
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }

            if (Directory.Exists(intermediate))
            {
                Directory.Delete(intermediate);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildHost_close_failure_preserves_the_success_or_original_save_failure(bool failSave)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var project = before.Projects.Single();
        var existing = project.Documents.First();
        var addedId = DocumentId.CreateNewId(project.Id);
        var addedPath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "Added.cs");
        const string Edit = "class Edited {}";
        const string Added = "class Added {}";
        var after = before.WithDocumentText(existing.Id, SourceText.From(Edit))
            .AddDocument(addedId, "Added.cs", SourceText.From(Added), filePath: addedPath);
        const string SaveError = "Project-save RPC disconnected.";
        const string CloseError = "Close RPC disconnected.";
        var closeAttempted = false;
        manager.AfterClosingProjectBuildHostForTests = () =>
        {
            closeAttempted = true;
            throw new Exception(CloseError);
        };
        if (failSave)
        {
            manager.BeforeSavingProjectRegistrationForTests = () => throw new Exception(SaveError);
        }

        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.True(closeAttempted);
        Assert.Equal(2, result.SavedPaths.Count);
        Assert.Contains(existing.FilePath, result.SavedPaths);
        Assert.Contains(addedPath, result.SavedPaths);
        Assert.Equal(Added, await File.ReadAllTextAsync(addedPath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(Edit, (await published.GetDocument(existing.Id)!.GetTextAsync()).ToString());
        var projectText = await File.ReadAllTextAsync(project.FilePath!);
        if (failSave)
        {
            Assert.Equal(WorkspaceWriteStatus.ReconciliationFailed, result.Status);
            Assert.Contains(SaveError, result.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(CloseError, result.Reason, StringComparison.Ordinal);
            Assert.Null(published.GetDocument(addedId));
            Assert.DoesNotContain("Include=\"Added.cs\"", projectText, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
            Assert.Equal(Added, (await published.GetDocument(addedId)!.GetTextAsync()).ToString());
            Assert.Contains("Include=\"Added.cs\"", projectText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Owned_backup_notifications_do_not_mark_the_baseline_changed()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var manager = fixture.Manager;
        await fixture.LoadAsync();
        manager.SilenceDiskWatchersForTests();
        await manager.Baseline.CaptureTask;
        var session = manager.DiskWatcherSession!;
        var backup = $".roslyn-mcp-write-{session.Generation:N}-{Guid.NewGuid():N}.backup.tmp";
        var reportedName = OperatingSystem.IsWindows() ? backup.ToUpperInvariant() + ".tmp" : backup;
        var path = Path.Combine(fixture.Root, reportedName);

        Assert.True(WorkspaceFilePersistence.IsStagingPath(session, path));
        await File.WriteAllTextAsync(path, "owned backup");
        manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(WatcherChangeTypes.Created, fixture.Root, reportedName));
        File.Delete(path);
        manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(WatcherChangeTypes.Deleted, fixture.Root, reportedName));

        Assert.DoesNotContain(path, manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        Assert.False(manager.ProjectGraphStale);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task CreateDirectoryAliasAsync(string path, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(path, target);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", path, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    // Exercise the timed persistence stage without Roslyn's subsequent workspace publication,
    // which has its own text reads.
    private static PersistChanges BindPersistence(SolutionManager manager) => typeof(SolutionManager)
        .GetMethod("PersistDocumentChangesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<PersistChanges>(manager);

    private static DocumentInfo CreateDocument(DocumentId id, string path, ReadCounter reads) =>
        DocumentInfo.Create(id, Path.GetFileName(path), loader: new CountingTextLoader(path, reads), filePath: path);

    private delegate Task PersistChanges(
        Solution before,
        Solution after,
        List<string> saved,
        List<(string Path, string Text, Encoding WriteEncoding)> savedTexts,
        PatchFileText? checkedPatchSource,
        CancellationToken cancellationToken);

    private sealed class CountingTextLoader(string path, ReadCounter reads) : TextLoader
    {
        public override async Task<TextAndVersion> LoadTextAndVersionAsync(
            LoadTextOptions options,
            CancellationToken cancellationToken)
        {
            var loaded = await _loader.LoadTextAndVersionAsync(options, cancellationToken);
            reads.Loads++;
            // The fixture uses only ASCII and no BOM.
            reads.Bytes += loaded.Text.Length;
            return loaded;
        }

        private readonly FileTextLoader _loader = new(path, defaultEncoding: null);
    }

    private sealed class ReadCounter
    {
        public int Loads { get; set; }
        public int Bytes { get; set; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoslynMcpPersist-" + Guid.NewGuid().ToString("N"));
}
