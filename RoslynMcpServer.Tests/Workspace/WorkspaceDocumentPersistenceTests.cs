using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
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
            await persist(before, after, saved, [], CancellationToken.None);
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
        var result = await manager.ApplySolutionChangesToDiskAsync(before, after, CancellationToken.None);

        Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
        Assert.Equal(2, result.SavedPaths.Count);
        Assert.Contains(sharedPath, result.SavedPaths);
        Assert.Contains(addedPath, result.SavedPaths);
        Assert.Equal(SharedEdit, await File.ReadAllTextAsync(sharedPath));
        Assert.Equal(AddedText, await File.ReadAllTextAsync(addedPath));
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
