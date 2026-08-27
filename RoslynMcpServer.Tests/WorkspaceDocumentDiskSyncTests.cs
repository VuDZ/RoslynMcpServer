using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceDocumentDiskSyncTests
{
    [Fact]
    public async Task ApplyAsync_updates_existing_document_from_disk()
    {
        using var ctx = TempProject.Create();
        File.WriteAllText(ctx.SourcePath, "class A { public int X; }");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Added);
        var text = await result.Solution.GetDocument(ctx.DocumentId)!.GetTextAsync();
        Assert.Contains("public int X", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_skips_unchanged_text()
    {
        using var ctx = TempProject.Create();

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Unchanged);
        Assert.Same(ctx.Workspace.CurrentSolution, result.Solution);
    }

    [Fact]
    public async Task ApplyAsync_adds_new_cs_under_project_directory()
    {
        using var ctx = TempProject.Create();
        var extra = Path.Combine(ctx.Root, "B.cs");
        File.WriteAllText(extra, "class B {}");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { extra },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, result.Added);
        Assert.Contains(
            result.Solution.Projects.SelectMany(p => p.Documents),
            d => string.Equals(d.FilePath, extra, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyAsync_removes_document_when_file_deleted()
    {
        using var ctx = TempProject.Create();
        File.Delete(ctx.SourcePath);

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, result.Removed);
        Assert.Null(result.Solution.GetDocument(ctx.DocumentId));
    }

    [Fact]
    public async Task ApplyAsync_ignores_bin_paths()
    {
        using var ctx = TempProject.Create();
        var binDir = Path.Combine(ctx.Root, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        var binCs = Path.Combine(binDir, "Generated.cs");
        File.WriteAllText(binCs, "class Generated {}");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { binCs },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, result.Added);
        Assert.DoesNotContain(
            result.Solution.Projects.SelectMany(p => p.Documents),
            d => string.Equals(d.FilePath, binCs, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TempProject : IDisposable
    {
        private TempProject(AdhocWorkspace workspace, string root, string sourcePath, DocumentId documentId)
        {
            Workspace = workspace;
            Root = root;
            SourcePath = sourcePath;
            DocumentId = documentId;
        }

        public AdhocWorkspace Workspace { get; }
        public string Root { get; }
        public string SourcePath { get; }
        public DocumentId DocumentId { get; }

        public static TempProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDiskSync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var csproj = Path.Combine(root, "P.csproj");
            var source = Path.Combine(root, "A.cs");
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(source, "class A {}");

            var workspace = new AdhocWorkspace();
            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "P",
                "P",
                LanguageNames.CSharp,
                filePath: csproj);
            var project = workspace.AddProject(projectInfo);
            var document = project.AddDocument("A.cs", SourceText.From("class A {}"), filePath: source);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            var applied = workspace.CurrentSolution.GetDocument(document.Id)
                ?? throw new InvalidOperationException("Document missing after apply.");
            return new TempProject(workspace, root, source, applied.Id);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
