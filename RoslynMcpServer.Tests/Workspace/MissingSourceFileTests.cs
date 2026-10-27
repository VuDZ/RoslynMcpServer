using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

/// <summary>
/// A generated file linked into many projects and missing on disk cost about one second per read: Roslyn
/// retries a missing file six times 200 ms apart before it returns empty text.
/// </summary>
public sealed class MissingSourceFileTests
{
    [Fact]
    public async Task Missing_file_reads_as_the_same_empty_text_without_the_retry_delay()
    {
        var root = CreateRoot();
        try
        {
            var missingPath = Path.Combine(root, "Generated", "VersionInfo.cs");
            var presentPath = Path.Combine(root, "Present.cs");
            await File.WriteAllTextAsync(presentPath, "class Present { }");
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("App", LanguageNames.CSharp);
            var missingId = AddFileDocument(workspace, project.Id, missingPath);
            AddFileDocument(workspace, project.Id, presentPath);
            // Another path: documents with one path share text that is already loaded, so the control
            // would not read the file at all.
            var retriedId = AddFileDocument(workspace, project.Id, Path.Combine(root, "Generated", "Control.cs"));

            var missing = MissingSourceFiles.Find(workspace.CurrentSolution, CancellationToken.None);
            Assert.Equal(new[] { missingId, retriedId }, missing.Select(document => document.Id));
            Assert.True(MissingSourceFiles.TryInstallLoaders(workspace, missing.Where(document => document.Id == missingId).ToArray(), out _));

            var watch = Stopwatch.StartNew();
            var fast = await workspace.CurrentSolution.GetDocument(missingId)!.GetTextAsync();
            var fastElapsed = watch.Elapsed;
            watch.Restart();
            var retried = await workspace.CurrentSolution.GetDocument(retriedId)!.GetTextAsync();
            var retriedElapsed = watch.Elapsed;

            Assert.Equal(string.Empty, fast.ToString());
            Assert.Equal(retried.ToString(), fast.ToString());
            Assert.True(fastElapsed < TimeSpan.FromMilliseconds(500), $"Missing file took {fastElapsed.TotalMilliseconds} ms.");
            Assert.True(retriedElapsed >= TimeSpan.FromMilliseconds(900), $"Roslyn no longer retries a missing file: {retriedElapsed.TotalMilliseconds} ms.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task File_created_after_load_is_read_from_disk()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "VersionInfo.cs");
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("App", LanguageNames.CSharp);
            var documentId = AddFileDocument(workspace, project.Id, path);
            Assert.True(MissingSourceFiles.TryInstallLoaders(
                workspace,
                MissingSourceFiles.Find(workspace.CurrentSolution, CancellationToken.None),
                out _));

            await File.WriteAllTextAsync(path, GeneratedSource);

            Assert.Equal(GeneratedSource, (await workspace.CurrentSolution.GetDocument(documentId)!.GetTextAsync()).ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_path_that_differs_only_in_case_from_an_existing_file_keeps_its_loader()
    {
        var root = CreateRoot();
        try
        {
            var existingPath = Path.Combine(root, "Shared", "Foo.cs");
            var missingPath = Path.Combine(root, "Shared", "foo.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
            await File.WriteAllTextAsync(existingPath, GeneratedSource);
            if (File.Exists(missingPath))
            {
                // A case-insensitive file system: both paths name the existing file.
                return;
            }

            using var workspace = new AdhocWorkspace();
            var existingId = AddFileDocument(workspace, workspace.AddProject("Left", LanguageNames.CSharp).Id, existingPath);
            AddFileDocument(workspace, workspace.AddProject("Right", LanguageNames.CSharp).Id, missingPath);

            // Roslyn relates documents by path ignoring case and would pass a new loader of foo.cs to Foo.cs.
            var missing = MissingSourceFiles.Find(workspace.CurrentSolution, CancellationToken.None);
            Assert.Empty(missing);

            Assert.True(MissingSourceFiles.TryInstallLoaders(workspace, missing, out _));
            Assert.Equal(GeneratedSource, (await workspace.CurrentSolution.GetDocument(existingId)!.GetTextAsync()).ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_additional_and_analyzer_config_files_get_the_fast_loader()
    {
        MsBuildBootstrapper.Register();
        var root = CreateRoot();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            var projectPath = Path.Combine(root, "App.csproj");
            await File.WriteAllTextAsync(projectPath, ProjectWithMissingNonCompileItems, BomFree);
            await File.WriteAllTextAsync(Path.Combine(root, "Class1.cs"), "namespace App; public static class Class1 { }", BomFree);
            await manager.LoadAndPrepareAsync(projectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

            var line = Assert.Single(logger.Messages, message => message.StartsWith("workspace_missing_source_files ", StringComparison.Ordinal));
            Assert.Contains("\"available\":true", line, StringComparison.Ordinal);
            Assert.Contains("\"installed\":true", line, StringComparison.Ordinal);
            var published = await manager.GetPublishedSolutionAsync();
            Assert.NotNull(published);
            var project = Assert.Single(published.Projects);
            var additional = Assert.Single(project.AdditionalDocuments, document => IsAt(document, Path.Combine(root, "missing.json")));
            var analyzerConfig = Assert.Single(project.AnalyzerConfigDocuments, document => IsAt(document, Path.Combine(root, "missing.editorconfig")));

            var watch = Stopwatch.StartNew();
            Assert.Equal(string.Empty, (await additional.GetTextAsync()).ToString());
            Assert.Equal(string.Empty, (await analyzerConfig.GetTextAsync()).ToString());
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"Missing non-compile items took {watch.Elapsed.TotalMilliseconds} ms.");
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A locked file only affects cleanup of a temporary directory.
            }
        }

        static bool IsAt(TextDocument document, string path) =>
            string.Equals(document.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_compile_item_is_fast_never_written_by_a_tool_write_and_synced_when_created()
    {
        MsBuildBootstrapper.Register();
        var root = CreateRoot();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            var projectPath = Path.Combine(root, "App.csproj");
            var generatedPath = Path.Combine(root, "Generated", "VersionInfo.cs");
            await File.WriteAllTextAsync(projectPath, ProjectWithMissingCompileItem, BomFree);
            await File.WriteAllTextAsync(Path.Combine(root, "Class1.cs"), "namespace App; public static class Class1 { }", BomFree);
            await manager.LoadAndPrepareAsync(projectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

            Assert.Contains(
                logger.Messages,
                message => message.StartsWith("workspace_missing_source_files ", StringComparison.Ordinal)
                    && message.Contains("\"documents\":1,", StringComparison.Ordinal)
                    && message.Contains("\"installed\":true", StringComparison.Ordinal));
            var published = await manager.GetPublishedSolutionAsync();
            Assert.NotNull(published);
            var watch = Stopwatch.StartNew();
            Assert.Equal(string.Empty, (await DocumentAt(published, generatedPath).GetTextAsync()).ToString());
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"Missing compile item took {watch.Elapsed.TotalMilliseconds} ms.");

            // A tool write starts from the published snapshot. The missing document must not be part of it,
            // or MSBuildWorkspace would create the file with the empty text.
            var class1 = DocumentAt(published, Path.Combine(root, "Class1.cs"));
            var edited = published.WithDocumentText(class1.Id, SourceText.From("namespace App; public static class Class1 { public static int N() => 1; }"));
            var write = await manager.ApplySolutionChangesToDiskAsync(published, edited, CancellationToken.None);
            Assert.True(write.WorkspaceApplied, $"Write status {write.Status}: {write.Reason}");
            Assert.False(File.Exists(generatedPath), "A tool write created the missing file.");
            Assert.Contains("N()", await File.ReadAllTextAsync(Path.Combine(root, "Class1.cs")), StringComparison.Ordinal);

            Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
            await File.WriteAllTextAsync(generatedPath, GeneratedSource, BomFree);
            var session = manager.DiskWatcherSession;
            Assert.NotNull(session);
            manager.NotifyDiskWatcherChange(
                session,
                new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(generatedPath)!, Path.GetFileName(generatedPath)));
            var synced = await FlushUntilAsync(
                manager,
                async solution => (await DocumentAt(solution, generatedPath).GetTextAsync()).ToString() == GeneratedSource);
            Assert.Equal(GeneratedSource, (await DocumentAt(synced, generatedPath).GetTextAsync()).ToString());
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A locked file only affects cleanup of a temporary directory.
            }
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpMissingSource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static DocumentId AddFileDocument(AdhocWorkspace workspace, ProjectId projectId, string path)
    {
        var info = DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            Path.GetFileName(path),
            loader: new FileTextLoader(path, defaultEncoding: null),
            filePath: path);
        return workspace.AddDocument(info).Id;
    }

    private static Document DocumentAt(Solution solution, string path) =>
        solution.Projects.SelectMany(project => project.Documents)
            .Single(document => string.Equals(document.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Real watchers can deliver the event after a flush read the file; by contract the path then waits for
    /// the next flush, so a single flush is not enough.
    /// </summary>
    private static async Task<Solution> FlushUntilAsync(SolutionManager manager, Func<Solution, Task<bool>> settled)
    {
        Solution? synced = null;
        for (var flush = 0; flush < MaxSettleFlushes; flush++)
        {
            synced = await manager.GetPublishedSolutionAfterDiskSyncAsync();
            Assert.NotNull(synced);
            if (await settled(synced))
            {
                break;
            }

            await Task.Delay(SettleDelayMilliseconds);
        }

        return synced!;
    }

    private const int MaxSettleFlushes = 20;
    private const int SettleDelayMilliseconds = 25;

    private const string GeneratedSource = "namespace App; public static class VersionInfo { public const string Value = \"1\"; }";

    private const string ProjectWithMissingCompileItem = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Generated\VersionInfo.cs" />
          </ItemGroup>
        </Project>
        """;

    private const string ProjectWithMissingNonCompileItems = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <AdditionalFiles Include="missing.json" />
            <EditorConfigFiles Include="missing.editorconfig" />
          </ItemGroup>
        </Project>
        """;

    private static readonly UTF8Encoding BomFree = new(encoderShouldEmitUTF8Identifier: false);
}
