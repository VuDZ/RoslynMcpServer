using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceNonCSharpDiskSyncTests
{
    [Fact]
    public void Syncs_additional_file_and_analyzer_config_but_not_markup_or_evaluation()
    {
        var root = Path.Combine(Path.GetTempPath(), "ncs-role-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload.txt");
        var config = Path.Combine(root, ".editorconfig");
        var xaml = Path.Combine(root, "MainWindow.xaml");
        var resx = Path.Combine(root, "Strings.resx");
        var razor = Path.Combine(root, "Page.razor");
        var props = Path.Combine(root, "Directory.Build.props");
        var projectId = ProjectId.CreateNewId();
        var map = Map(
            Entry(payload, projectId, InputRole.AdditionalFile),
            Entry(config, projectId, InputRole.AnalyzerConfig),
            Entry(xaml, projectId, InputRole.AdditionalFile),
            Entry(resx, projectId, InputRole.AdditionalFile),
            Entry(razor, projectId, InputRole.AdditionalFile),
            Entry(props, projectId, InputRole.EvaluationInput),
            Entry(Path.Combine(root, "both.txt"), projectId, InputRole.AdditionalFile, InputRole.EvaluationInput));

        Assert.True(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, payload));
        Assert.True(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, config));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, xaml));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, resx));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, razor));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, props));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, Path.Combine(root, "both.txt")));
        Assert.False(WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(map, Path.Combine(root, "missing.txt")));
    }

    [Fact]
    public async Task Apply_updates_every_project_of_one_path_and_leaves_other_documents()
    {
        var payload = Path.Combine(Path.GetTempPath(), "ncs-payload-" + Guid.NewGuid().ToString("N") + ".txt");
        var config = Path.Combine(Path.GetTempPath(), "ncs-config-" + Guid.NewGuid().ToString("N") + ".editorconfig");
        var xaml = Path.Combine(Path.GetTempPath(), "ncs-window-" + Guid.NewGuid().ToString("N") + ".xaml");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(first, VersionStamp.Create(), "A", "A", LanguageNames.CSharp))
            .AddProject(ProjectInfo.Create(second, VersionStamp.Create(), "B", "B", LanguageNames.CSharp));
        solution = solution
            .AddAdditionalDocument(DocumentId.CreateNewId(first), "payload.txt", SourceText.From("OFF"), filePath: payload)
            .AddAdditionalDocument(DocumentId.CreateNewId(second), "payload.txt", SourceText.From("OFF"), filePath: payload)
            .AddAdditionalDocument(DocumentId.CreateNewId(first), "MainWindow.xaml", SourceText.From("OLD"), filePath: xaml);
        solution = solution.GetProject(first)!
            .AddAnalyzerConfigDocument(".editorconfig", SourceText.From("none"), filePath: config)
            .Project.Solution;

        var applied = WorkspaceNonCSharpDiskSync.Apply(
            solution,
            new Dictionary<string, SourceText>(InputPathCanon.Comparer)
            {
                [payload] = SourceText.From("ON", SourceTextEncoding.BomFreeUtf8),
                [config] = SourceText.From("error", SourceTextEncoding.BomFreeUtf8),
            },
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new[] { "ON", "ON" }, await TextsAsync(applied, payload));
        Assert.Equal("error", (await TextsAsync(applied, config)).Single());
        Assert.Equal("OLD", (await TextsAsync(applied, xaml)).Single());
    }

    [Fact]
    public async Task Read_skips_a_missing_file_and_a_path_without_a_document()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ncs-missing-" + Guid.NewGuid().ToString("N") + ".txt");
        var absent = Path.Combine(Path.GetTempPath(), "ncs-absent-" + Guid.NewGuid().ToString("N") + ".txt");
        var projectId = ProjectId.CreateNewId();
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "A", "A", LanguageNames.CSharp))
            .AddAdditionalDocument(DocumentId.CreateNewId(projectId), "missing.txt", SourceText.From("OFF"), filePath: missing);

        var updates = await WorkspaceNonCSharpDiskSync.ReadUpdatesAsync(
            solution,
            new[] { missing, absent },
            StringComparison.OrdinalIgnoreCase,
            CancellationToken.None);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task Saved_additional_file_and_editorconfig_reach_the_published_snapshot()
    {
        await using var project = await SpikeProject.CreateAsync();
        var published = await project.Manager.GetPublishedSolutionAsync();
        Assert.NotNull(published);
        var payload = Document(published, "payload.txt");
        var config = Document(published, ".editorconfig");
        var xaml = Document(published, "MainWindow.xaml");
        Assert.NotNull(payload);
        Assert.NotNull(config);
        Assert.NotNull(xaml);
        Assert.Equal("OFF", (await payload.GetTextAsync()).ToString());
        Assert.Contains("severity = none", (await config.GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Equal("OLD", (await xaml.GetTextAsync()).ToString());
        Assert.Empty(await UnusedAsync(published));

        await File.WriteAllTextAsync(project.PathFor("payload.txt"), "ON", BomFree);
        await File.WriteAllTextAsync(project.PathFor(".editorconfig"), ReportedConfig, BomFree);
        await File.WriteAllTextAsync(project.PathFor("MainWindow.xaml"), "NEW", BomFree);
        Notify(project, "payload.txt");
        Notify(project, ".editorconfig");
        Notify(project, "MainWindow.xaml");

        var synced = await project.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(synced);
        Assert.Equal("ON", (await Document(synced, "payload.txt")!.GetTextAsync()).ToString());
        Assert.Contains("severity = error", (await Document(synced, ".editorconfig")!.GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Equal("OLD", (await Document(synced, "MainWindow.xaml")!.GetTextAsync()).ToString());
        Assert.Contains(await UnusedAsync(synced), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(project.ProjectBytes, await File.ReadAllBytesAsync(project.PathFor("App.csproj")));
        Assert.Equal(BomFree.GetBytes("ON"), await File.ReadAllBytesAsync(project.PathFor("payload.txt")));
        Assert.Equal(BomFree.GetBytes("NEW"), await File.ReadAllBytesAsync(project.PathFor("MainWindow.xaml")));

        await File.WriteAllTextAsync(
            project.PathFor("Class1.cs"),
            "namespace App; public static class Class1 { public static int N() => 1; }",
            BomFree);
        Notify(project, "Class1.cs");
        var afterCSharp = await project.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(afterCSharp);
        Assert.Equal("ON", (await Document(afterCSharp, "payload.txt")!.GetTextAsync()).ToString());
        var source = afterCSharp.Projects.Single().Documents
            .Single(document => document.FilePath?.EndsWith("Class1.cs", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("N()", (await source.GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Equal(project.ProjectBytes, await File.ReadAllBytesAsync(project.PathFor("App.csproj")));

        await project.Manager.WriteTrackedTextAsync(project.PathFor("payload.txt"), "OWN", BomFree, CancellationToken.None);
        var owned = await project.Manager.GetPublishedSolutionAsync();
        Assert.NotNull(owned);
        Assert.Equal("OWN", (await Document(owned, "payload.txt")!.GetTextAsync()).ToString());
        Assert.Equal(BomFree.GetBytes("OWN"), await File.ReadAllBytesAsync(project.PathFor("payload.txt")));
        Assert.Equal(project.ProjectBytes, await File.ReadAllBytesAsync(project.PathFor("App.csproj")));
    }

    private static TextDocument? Document(Solution solution, string name)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.AdditionalDocuments.Concat<TextDocument>(project.AnalyzerConfigDocuments))
            {
                if (document.FilePath?.EndsWith(name, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return document;
                }
            }
        }

        return null;
    }

    private static async Task<string[]> TextsAsync(Solution solution, string fullPath)
    {
        var texts = new List<string>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.AdditionalDocuments.Concat<TextDocument>(project.AnalyzerConfigDocuments))
            {
                if (!string.Equals(document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                texts.Add((await document.GetTextAsync()).ToString());
            }
        }

        return texts.ToArray();
    }

    private static async Task<IReadOnlyList<Diagnostic>> UnusedAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == "CS0219" && diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
    }

    private static void Notify(SpikeProject project, string name)
    {
        var path = project.PathFor(name);
        var session = project.Manager.DiskWatcherSession;
        Assert.NotNull(session);
        project.Manager.NotifyDiskWatcherChange(
            session,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    private static InputPathEntry Entry(string path, ProjectId projectId, params InputRole[] roles)
    {
        var occurrences = roles.Select(role => new InputOccurrence(projectId, DocumentId: null, role, ProducerProjectId: null, Present: true)).ToArray();
        return new InputPathEntry(Path.GetFullPath(path), occurrences);
    }

    private static WorkspaceInputMap Map(params InputPathEntry[] entries)
    {
        var projectId = entries[0].Occurrences[0].ProjectId;
        return new WorkspaceInputMap(
            Guid.NewGuid(),
            loadGraphComplete: true,
            InputCoverage.Complete,
            InputCoverage.Complete,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[]
            {
                new LoadedProjectInstance(projectId, "App.csproj", "App", "App", Array.Empty<ProjectId>(), new[] { "net10.0" }),
            },
            entries,
            Array.Empty<MembershipRegion>(),
            Array.Empty<InputWatcherDescriptor>(),
            Array.Empty<InputCategoryEvidence>(),
            Array.Empty<string>());
    }

    private const string ReportedConfig = """
        root = true

        [*.cs]
        dotnet_diagnostic.CS0219.severity = error
        """;

    private static readonly UTF8Encoding BomFree = new(encoderShouldEmitUTF8Identifier: false);

    private sealed class SpikeProject : IAsyncDisposable
    {
        private SpikeProject(SolutionManager manager, string root, byte[] projectBytes)
        {
            Manager = manager;
            Root = root;
            ProjectBytes = projectBytes;
        }

        public SolutionManager Manager { get; }

        public string Root { get; }

        public byte[] ProjectBytes { get; }

        public static async Task<SpikeProject> CreateAsync()
        {
            MsBuildBootstrapper.Register();
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpNonCSharpSync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "App.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <AdditionalFiles Include="payload.txt" />
                    <AdditionalFiles Include="MainWindow.xaml" />
                  </ItemGroup>
                </Project>
                """,
                BomFree);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Class1.cs"),
                """
                namespace App;
                public static class Class1
                {
                    public static int M()
                    {
                        int unused = 1;
                        return 0;
                    }
                }
                """,
                BomFree);
            await File.WriteAllTextAsync(Path.Combine(root, "payload.txt"), "OFF", BomFree);
            await File.WriteAllTextAsync(Path.Combine(root, "MainWindow.xaml"), "OLD", BomFree);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                """
                root = true

                [*.cs]
                dotnet_diagnostic.CS0219.severity = none
                """,
                BomFree);
            var manager = SolutionManagerTestFactory.Create();
            await manager.LoadAndPrepareAsync(projectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            return new SpikeProject(manager, root, await File.ReadAllBytesAsync(projectPath));
        }

        public string PathFor(string name) => Path.Combine(Root, name);

        public async ValueTask DisposeAsync()
        {
            await Manager.ClearWorkspaceAsync();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
