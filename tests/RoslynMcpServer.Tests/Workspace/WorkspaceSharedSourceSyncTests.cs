using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceSharedSourceSyncTests
{
    [Fact]
    public async Task One_disk_read_updates_every_user_document_of_a_shared_path()
    {
        using var fixture = SharedFixture.Create(includeGenerated: false);
        await File.WriteAllTextAsync(fixture.SharedPath, "class SharedFile { public int CommonAfter; }");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            fixture.Workspace.CurrentSolution,
            new[] { fixture.SharedPath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase,
            fixture.Map,
            CancellationToken.None);

        Assert.Equal(2, result.Updated);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        var texts = new List<string>();
        foreach (var documentId in fixture.UserDocumentIds)
        {
            var text = await result.Solution.GetDocument(documentId)!.GetTextAsync();
            texts.Add(text.ToString());
            Assert.Contains("CommonAfter", text.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(texts[0], texts[1]);
        Assert.Equal(2, result.Solution.Projects.SelectMany(project => project.Documents).Count());
    }

    [Fact]
    public async Task Project_specific_parse_options_stay_with_their_project()
    {
        using var fixture = SharedFixture.Create(includeGenerated: false);
        await File.WriteAllTextAsync(
            fixture.SharedPath,
            """
            class SharedFile
            {
                public int CommonAfter;
            #if APP
                public int AppOnly;
            #endif
            #if OTHER
                public int OtherOnly;
            #endif
            }
            """);

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            fixture.Workspace.CurrentSolution,
            new[] { fixture.SharedPath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase,
            fixture.Map,
            CancellationToken.None);

        var appCompilation = await result.Solution.GetProject(fixture.AppId)!.GetCompilationAsync();
        var otherCompilation = await result.Solution.GetProject(fixture.OtherId)!.GetCompilationAsync();
        var appType = appCompilation!.GetTypeByMetadataName("SharedFile");
        var otherType = otherCompilation!.GetTypeByMetadataName("SharedFile");
        Assert.NotNull(appType);
        Assert.NotNull(otherType);
        Assert.NotEmpty(appType.GetMembers("AppOnly"));
        Assert.Empty(appType.GetMembers("OtherOnly"));
        Assert.NotEmpty(otherType.GetMembers("OtherOnly"));
        Assert.Empty(otherType.GetMembers("AppOnly"));
        Assert.NotEmpty(appType.GetMembers("CommonAfter"));
        Assert.NotEmpty(otherType.GetMembers("CommonAfter"));
    }

    [Fact]
    public async Task Generated_document_of_the_same_path_is_not_text_synced()
    {
        using var fixture = SharedFixture.Create(includeGenerated: true);
        var generatedBefore = await fixture.Workspace.CurrentSolution.GetDocument(fixture.GeneratedDocumentId)!.GetTextAsync();
        await File.WriteAllTextAsync(fixture.SharedPath, "class SharedFile { public int FromDisk; }");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            fixture.Workspace.CurrentSolution,
            new[] { fixture.SharedPath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase,
            fixture.Map,
            CancellationToken.None);

        var userText = await result.Solution.GetDocument(fixture.UserDocumentIds[0])!.GetTextAsync();
        var generatedText = await result.Solution.GetDocument(fixture.GeneratedDocumentId)!.GetTextAsync();
        Assert.Contains("FromDisk", userText.ToString(), StringComparison.Ordinal);
        Assert.Equal(generatedBefore.ToString(), generatedText.ToString());
        Assert.Equal(fixture.Workspace.CurrentSolution.ProjectIds.Count, result.Solution.ProjectIds.Count);
    }

    [Fact]
    public async Task Confirmed_text_keeps_the_file_bom_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpSharedBom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bomFreePath = Path.Combine(root, "Free.cs");
            var bomPath = Path.Combine(root, "Bom.cs");
            var projectPath = Path.Combine(root, "P.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project />");
            await File.WriteAllTextAsync(bomFreePath, "class Free { public int Old; }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await File.WriteAllTextAsync(bomPath, "class Bom { public int Old; }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "P",
                "P",
                LanguageNames.CSharp,
                filePath: projectPath));
            var freeId = DocumentId.CreateNewId(projectId);
            var bomId = DocumentId.CreateNewId(projectId);
            var solution = workspace.CurrentSolution
                .AddDocument(freeId, "Free.cs", SourceText.From("class Free { public int Old; }"), filePath: bomFreePath)
                .AddDocument(bomId, "Bom.cs", SourceText.From("class Bom { public int Old; }"), filePath: bomPath);
            Assert.True(workspace.TryApplyChanges(solution));

            await File.WriteAllTextAsync(bomFreePath, "class Free { public int New; }", new UTF8Encoding(false));
            await File.WriteAllTextAsync(bomPath, "class Bom { public int New; }", new UTF8Encoding(true));
            var map = WorkspaceInputMapBuilder.Build(workspace.CurrentSolution, Guid.NewGuid(), projectPath, loadGraphComplete: true);
            var result = await WorkspaceDocumentDiskSync.ApplyAsync(
                workspace.CurrentSolution,
                new[] { bomFreePath, bomPath },
                refreshAllDocuments: false,
                StringComparison.OrdinalIgnoreCase,
                map,
                CancellationToken.None);

            var freeText = await result.Solution.GetDocument(freeId)!.GetTextAsync();
            var bomText = await result.Solution.GetDocument(bomId)!.GetTextAsync();
            Assert.Contains("New", freeText.ToString(), StringComparison.Ordinal);
            Assert.Contains("New", bomText.ToString(), StringComparison.Ordinal);
            Assert.Empty(freeText.Encoding!.GetPreamble());
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bomText.Encoding!.GetPreamble());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class SharedFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace;
        private readonly string _root;

        private SharedFixture(
            AdhocWorkspace workspace,
            string root,
            string sharedPath,
            ProjectId appId,
            ProjectId otherId,
            IReadOnlyList<DocumentId> userDocumentIds,
            DocumentId generatedDocumentId,
            WorkspaceInputMap map)
        {
            _workspace = workspace;
            _root = root;
            SharedPath = sharedPath;
            AppId = appId;
            OtherId = otherId;
            UserDocumentIds = userDocumentIds;
            GeneratedDocumentId = generatedDocumentId;
            Map = map;
        }

        public AdhocWorkspace Workspace => _workspace;

        public string SharedPath { get; }

        public ProjectId AppId { get; }

        public ProjectId OtherId { get; }

        public IReadOnlyList<DocumentId> UserDocumentIds { get; }

        public DocumentId GeneratedDocumentId { get; }

        public WorkspaceInputMap Map { get; }

        public static SharedFixture Create(bool includeGenerated)
        {
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpSharedSync-" + Guid.NewGuid().ToString("N"));
            var appDir = Path.Combine(root, "app");
            var otherDir = Path.Combine(root, "other");
            var linked = Path.Combine(root, "linked");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(otherDir);
            Directory.CreateDirectory(linked);
            var sharedPath = Path.Combine(linked, "Shared.cs");
            var appProject = Path.Combine(appDir, "App.csproj");
            var otherProject = Path.Combine(otherDir, "Other.csproj");
            File.WriteAllText(appProject, "<Project />");
            File.WriteAllText(otherProject, "<Project />");
            File.WriteAllText(sharedPath, "class SharedFile { public int Common; }");

            var workspace = new AdhocWorkspace();
            var appId = ProjectId.CreateNewId();
            var otherId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(
                appId,
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: appProject,
                parseOptions: new CSharpParseOptions(preprocessorSymbols: ["APP"])));
            workspace.AddProject(ProjectInfo.Create(
                otherId,
                VersionStamp.Create(),
                "Other",
                "Other",
                LanguageNames.CSharp,
                filePath: otherProject,
                parseOptions: new CSharpParseOptions(preprocessorSymbols: ["OTHER"])));

            var appDocument = DocumentId.CreateNewId(appId);
            var otherDocument = DocumentId.CreateNewId(otherId);
            var generatedDocument = DocumentId.CreateNewId(appId);
            var solution = workspace.CurrentSolution
                .AddDocument(appDocument, "Shared.cs", SourceText.From("class SharedFile { public int Common; }"), filePath: sharedPath)
                .AddDocument(otherDocument, "Shared.cs", SourceText.From("class SharedFile { public int Common; }"), filePath: sharedPath);
            if (includeGenerated)
            {
                solution = solution.AddDocument(
                    generatedDocument,
                    "Shared.g.cs",
                    SourceText.From("class SharedFile { public int GeneratedOld; }"),
                    filePath: sharedPath,
                    isGenerated: true);
            }

            if (!workspace.TryApplyChanges(solution))
            {
                throw new InvalidOperationException("Applying the shared-source fixture failed.");
            }

            var map = WorkspaceInputMapBuilder.Build(
                workspace.CurrentSolution,
                Guid.NewGuid(),
                Path.Combine(root, "App.sln"),
                loadGraphComplete: true);
            return new SharedFixture(
                workspace,
                root,
                sharedPath,
                appId,
                otherId,
                new[] { appDocument, otherDocument },
                generatedDocument,
                map);
        }

        public void Dispose()
        {
            _workspace.Dispose();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
