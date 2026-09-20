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
    public async Task ApplyAsync_refreshes_identity_when_text_is_unchanged()
    {
        using var ctx = TempProject.Create();

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Unchanged);
        var text = await result.Solution.GetDocument(ctx.DocumentId)!.GetTextAsync();
        Assert.Contains("class A", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_does_not_add_new_cs_under_project_directory()
    {
        using var ctx = TempProject.Create();
        var extra = Path.Combine(ctx.Root, "B.cs");
        File.WriteAllText(extra, "class B {}");
        var beforeCount = ctx.Workspace.CurrentSolution.Projects.SelectMany(p => p.Documents).Count();

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { extra },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, result.Added);
        Assert.Contains(result.Unrepresentable, p => string.Equals(p, extra, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeCount, result.Solution.Projects.SelectMany(p => p.Documents).Count());
        Assert.DoesNotContain(
            result.Solution.Projects.SelectMany(p => p.Documents),
            d => string.Equals(d.FilePath, extra, StringComparison.OrdinalIgnoreCase));
        Assert.True(ReferenceEquals(ctx.Workspace.CurrentSolution, result.Solution));
    }

    [Fact]
    public async Task ApplyAsync_does_not_remove_document_when_file_deleted()
    {
        using var ctx = TempProject.Create();
        var loaded = ctx.Workspace.CurrentSolution.GetDocument(ctx.DocumentId)!;
        _ = await loaded.GetTextAsync();
        Assert.True(loaded.TryGetText(out _));
        File.Delete(ctx.SourcePath);

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, result.Removed);
        Assert.NotNull(result.Solution.GetDocument(ctx.DocumentId));
        Assert.Contains(
            result.Unrepresentable,
            p => string.Equals(p, ctx.SourcePath, StringComparison.OrdinalIgnoreCase));
        Assert.True(ReferenceEquals(ctx.Workspace.CurrentSolution, result.Solution));
    }

    [Fact]
    public async Task ApplyAsync_updates_known_file_and_reports_new_file_unrepresentable()
    {
        using var ctx = TempProject.Create();
        File.WriteAllText(ctx.SourcePath, "class A { public int X; }");
        var extra = Path.Combine(ctx.Root, "B.cs");
        File.WriteAllText(extra, "class B {}");

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            ctx.Workspace.CurrentSolution,
            new[] { ctx.SourcePath, extra },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Single(result.Unrepresentable);
        Assert.Contains(result.Unrepresentable, p => string.Equals(p, extra, StringComparison.OrdinalIgnoreCase));
        var text = await result.Solution.GetDocument(ctx.DocumentId)!.GetTextAsync();
        Assert.Contains("public int X", text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Solution.Projects.SelectMany(p => p.Documents),
            d => string.Equals(d.FilePath, extra, StringComparison.OrdinalIgnoreCase));
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
