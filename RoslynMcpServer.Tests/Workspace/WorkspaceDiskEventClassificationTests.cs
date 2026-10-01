using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceDiskEventClassificationTests
{
    [Fact]
    public async Task New_removed_deleted_and_renamed_sources_mark_composition_without_adding_a_document()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var beforeDocuments = fixture.Workspace.CurrentSolution.Projects.SelectMany(project => project.Documents).Count();

        File.WriteAllText(fixture.FreshPath, "class Fresh {}");
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.FreshPath, WatcherChangeTypes.Created));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.RemovedPath, WatcherChangeTypes.Changed));
        File.Delete(fixture.ProgramPath);
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.ProgramPath, WatcherChangeTypes.Deleted));
        fixture.Manager.NotifyDiskWatcherRename(
            session,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.ProjectDirectory, "Renamed.cs", "Program.cs"));

        Assert.Contains(fixture.SessionComposition(session), path => Same(path, fixture.FreshPath));
        Assert.Contains(fixture.SessionComposition(session), path => Same(path, fixture.RemovedPath));
        Assert.Contains(fixture.SessionComposition(session), path => Same(path, fixture.ProgramPath));
        Assert.Contains(fixture.SessionComposition(session), path => Same(path, Path.Combine(fixture.ProjectDirectory, "Renamed.cs")));
        Assert.False(fixture.Map.Consult(fixture.RemovedPath).ProvenExcluded);

        var sync = await WorkspaceDocumentDiskSync.ApplyAsync(
            fixture.Workspace.CurrentSolution,
            new[] { fixture.FreshPath, fixture.RemovedPath },
            refreshAllDocuments: false,
            StringComparison.OrdinalIgnoreCase,
            fixture.Map,
            CancellationToken.None);
        Assert.Equal(0, sync.Added);
        Assert.Equal(beforeDocuments, sync.Solution.Projects.SelectMany(project => project.Documents).Count());
        Assert.DoesNotContain(
            sync.Solution.Projects.SelectMany(project => project.Documents),
            document => Same(document.FilePath, fixture.FreshPath));
        Assert.Equal(File.ReadAllText(fixture.ProjectFile), fixture.ProjectText);
    }

    [Fact]
    public void Unrelated_file_does_not_invalidate_when_irrelevance_is_proven()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var unrelated = Path.Combine(fixture.Root, "unrelated", "Nope.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        File.WriteAllText(unrelated, "class Nope {}");

        fixture.Manager.NotifyDiskWatcherChange(session, Change(unrelated, WatcherChangeTypes.Changed));

        Assert.DoesNotContain(fixture.SessionComposition(session), path => Same(path, unrelated));
        Assert.Empty(fixture.Manager.GetPendingDirtySourcePaths());
        Assert.DoesNotContain(session.Notices, notice => Same(notice.CanonicalPath, unrelated));
        Assert.False(fixture.Manager.ProjectGraphStale);
    }

    [Fact]
    public void Explicit_input_in_obj_is_a_user_edit_and_generated_output_and_unknown_are_not()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var beforeProducer = session.InputRevisionFor(fixture.AppId);
        var beforeConsumer = session.InputRevisionFor(fixture.OtherId);

        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.ManualPath, WatcherChangeTypes.Changed));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.GeneratedPath, WatcherChangeTypes.Changed));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.GeneratedPath, WatcherChangeTypes.Changed));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.OutputPath, WatcherChangeTypes.Changed));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.MysteryPath, WatcherChangeTypes.Created));
        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.MixedPath, WatcherChangeTypes.Changed));

        Assert.Contains(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.ManualPath));
        Assert.DoesNotContain(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.GeneratedPath));
        Assert.DoesNotContain(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.OutputPath));
        Assert.DoesNotContain(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.MysteryPath));
        Assert.True(session.InputRevisionFor(fixture.AppId) > beforeProducer);
        Assert.Equal(beforeConsumer, session.InputRevisionFor(fixture.OtherId) - CountUserNotices(session, fixture.MixedPath, fixture.OtherId));
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.GeneratedPath)
            && notice.Role == InputRole.Generated
            && notice.ProjectId == fixture.AppId
            && !notice.CountsAsInputRevision);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.OutputPath)
            && notice.Role == InputRole.Output
            && !notice.CountsAsInputRevision);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.MysteryPath)
            && notice.Role == InputRole.Unknown
            && !notice.CountsAsInputRevision);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.MixedPath)
            && notice.Role == InputRole.UserInput
            && notice.ProjectId == fixture.OtherId
            && notice.CountsAsInputRevision);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.MixedPath)
            && notice.Role == InputRole.Generated
            && notice.ProjectId == fixture.AppId
            && !notice.CountsAsInputRevision);
        Assert.DoesNotContain(fixture.SessionComposition(session), path => Same(path, fixture.GeneratedPath));
        Assert.False(fixture.Manager.ProjectGraphStaleFromGraphFile);
    }

    [Fact]
    public void Non_csharp_input_is_notified_and_not_text_synced()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var before = session.InputRevisionFor(fixture.AppId);

        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.XamlPath, WatcherChangeTypes.Changed));

        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, fixture.XamlPath)
            && notice.Role == InputRole.AdditionalFile
            && notice.CountsAsInputRevision);
        Assert.True(session.InputRevisionFor(fixture.AppId) > before);
        Assert.Empty(fixture.Manager.GetPendingDirtySourcePaths());
        Assert.DoesNotContain(fixture.SessionComposition(session), path => Same(path, fixture.XamlPath));
    }

    [Fact]
    public async Task Missing_directory_rename_inside_a_region_stays_unknown_after_re_read()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var oldPath = Path.Combine(fixture.ProjectDirectory, "GoneArea");
        var newPath = Path.Combine(fixture.ProjectDirectory, "GoneAreaNext");
        Assert.False(Directory.Exists(oldPath));
        Assert.False(Directory.Exists(newPath));

        fixture.Manager.NotifyDiskWatcherRename(
            session,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, fixture.ProjectDirectory, "GoneAreaNext", "GoneArea"));

        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership, session.ObservationUnknownReasons);
        Assert.Equal(InputCoverage.Unknown, session.ObservationCoverage);
        Assert.Contains(session.DirectoryRenamePaths, path => Same(path, oldPath));
        Assert.Contains(session.DirectoryRenamePaths, path => Same(path, newPath));

        await fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();

        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership, session.ObservationUnknownReasons);
        Assert.Equal(InputCoverage.Unknown, session.ObservationCoverage);
    }

    [Fact]
    public void Loose_neighbor_of_a_linked_file_is_unknown_and_is_not_a_search_root()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var loose = Path.Combine(fixture.LinkedDirectory, "Loose.cs");
        File.WriteAllText(loose, "class Loose {}");

        fixture.Manager.NotifyDiskWatcherChange(session, Change(loose, WatcherChangeTypes.Created));

        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, loose) && notice.Role == InputRole.Unknown);
        Assert.DoesNotContain(fixture.Map.SearchRoots, root => Same(root, fixture.LinkedDirectory));
        Assert.DoesNotContain(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, loose));
        var roots = UtilityTools.ResolveSearchRoots(
            directoryPath: null,
            fixture.SolutionPath,
            new[] { fixture.ProjectFile, fixture.OtherProject },
            processCurrentDirectory: fixture.Root);
        Assert.Equal(
            SolutionManager.ComputeWatchRoots(fixture.SolutionPath, new[] { fixture.ProjectFile, fixture.OtherProject }),
            roots);
        Assert.DoesNotContain(roots, root => Same(root, fixture.LinkedDirectory));
    }

    [Theory]
    [InlineData(".csproj")]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void Foreign_graph_file_in_a_point_watched_directory_is_unknown_without_graph_invalidation(string extension)
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var foreign = Path.Combine(fixture.LinkedDirectory, "Foreign" + extension);

        fixture.Manager.NotifyDiskWatcherChange(session, Change(foreign, WatcherChangeTypes.Created));

        Assert.False(fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.False(fixture.Manager.ProjectGraphStaleFromComposition);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, foreign) && notice.Role == InputRole.Unknown && !notice.CountsAsInputRevision);
        Assert.Contains(InputCoverageReason.UnknownRoleOrProducer, session.ObservationUnknownReasons);
    }

    [Theory]
    [InlineData(".csproj")]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void New_graph_file_inside_project_membership_still_invalidates_the_graph(string extension)
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();
        var graphFile = Path.Combine(fixture.ProjectDirectory, "New" + extension);

        fixture.Manager.NotifyDiskWatcherChange(session, Change(graphFile, WatcherChangeTypes.Created));

        Assert.True(fixture.Manager.ProjectGraphStaleFromGraphFile);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, graphFile) && notice.Role == InputRole.EvaluationInput && notice.CountsAsInputRevision);
    }

    [Fact]
    public void Indexed_solution_outside_project_membership_still_invalidates_the_graph()
    {
        using var fixture = MapFixture.Create();
        var session = fixture.Publish();

        fixture.Manager.NotifyDiskWatcherChange(session, Change(fixture.SolutionPath, WatcherChangeTypes.Changed));

        Assert.True(fixture.Manager.ProjectGraphStaleFromGraphFile);
    }

    private static int CountUserNotices(WorkspaceInputSession session, string path, ProjectId projectId)
    {
        return session.Notices.Count(notice =>
            Same(notice.CanonicalPath, path)
            && notice.Role == InputRole.UserInput
            && notice.ProjectId == projectId
            && notice.CountsAsInputRevision);
    }

    private static FileSystemEventArgs Change(string path, WatcherChangeTypes changeType)
    {
        return new FileSystemEventArgs(changeType, Path.GetDirectoryName(path)!, Path.GetFileName(path));
    }

    private static bool Same(string? left, string? right)
    {
        return left is not null
            && right is not null
            && InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    }

    private sealed class MapFixture : IDisposable
    {
        private readonly string _root;
        private readonly AdhocWorkspace _workspace;

        private MapFixture(
            string root,
            AdhocWorkspace workspace,
            SolutionManager manager,
            WorkspaceInputMap map,
            string projectText,
            ProjectId appId,
            ProjectId otherId)
        {
            _root = root;
            _workspace = workspace;
            Manager = manager;
            Map = map;
            ProjectText = projectText;
            AppId = appId;
            OtherId = otherId;
        }

        public string Root => _root;

        public AdhocWorkspace Workspace => _workspace;

        public SolutionManager Manager { get; }

        public WorkspaceInputMap Map { get; }

        public string ProjectText { get; }

        public string ProjectDirectory => Path.Combine(_root, "app");

        public string LinkedDirectory => Path.Combine(_root, "linked");

        public string ProjectFile => Path.Combine(ProjectDirectory, "App.csproj");

        public string OtherProject => Path.Combine(_root, "other", "Other.csproj");

        public string SolutionPath => Path.Combine(_root, "App.sln");

        public string ProgramPath => Path.Combine(ProjectDirectory, "Program.cs");

        public string FreshPath => Path.Combine(ProjectDirectory, "Fresh.cs");

        public string RemovedPath => Path.Combine(ProjectDirectory, "Removed.cs");

        public string ManualPath => Path.Combine(ProjectDirectory, "obj", "Manual.cs");

        public string GeneratedPath => Path.Combine(ProjectDirectory, "obj", "View.g.cs");

        public string MysteryPath => Path.Combine(ProjectDirectory, "obj", "Mystery.g.cs");

        public string OutputPath => Path.Combine(ProjectDirectory, "bin", "App.dll");

        public string MixedPath => Path.Combine(_root, "roles", "Mixed.cs");

        public string XamlPath => Path.Combine(ProjectDirectory, "MainWindow.xaml");

        public ProjectId AppId { get; }

        public ProjectId OtherId { get; }

        public static MapFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDiskEvent-" + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "app");
            var otherDirectory = Path.Combine(root, "other");
            var linked = Path.Combine(root, "linked");
            var roles = Path.Combine(root, "roles");
            Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "bin"));
            Directory.CreateDirectory(otherDirectory);
            Directory.CreateDirectory(linked);
            Directory.CreateDirectory(roles);

            var projectFile = Path.Combine(projectDirectory, "App.csproj");
            var otherProject = Path.Combine(otherDirectory, "Other.csproj");
            var projectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
            File.WriteAllText(projectFile, projectText);
            File.WriteAllText(otherProject, projectText);
            File.WriteAllText(Path.Combine(root, "App.sln"), "Microsoft Visual Studio Solution File, Format Version 12.00");
            var program = Path.Combine(projectDirectory, "Program.cs");
            var manual = Path.Combine(projectDirectory, "obj", "Manual.cs");
            var generated = Path.Combine(projectDirectory, "obj", "View.g.cs");
            var output = Path.Combine(projectDirectory, "bin", "App.dll");
            var mixed = Path.Combine(roles, "Mixed.cs");
            var xaml = Path.Combine(projectDirectory, "MainWindow.xaml");
            var shared = Path.Combine(linked, "Shared.cs");
            File.WriteAllText(program, "class Program {}");
            File.WriteAllText(Path.Combine(projectDirectory, "Removed.cs"), "class Removed {}");
            File.WriteAllText(manual, "class Manual {}");
            File.WriteAllText(generated, "class Generated {}");
            File.WriteAllText(output, "output");
            File.WriteAllText(mixed, "class Mixed {}");
            File.WriteAllText(xaml, "<Window />");
            File.WriteAllText(shared, "class Shared {}");

            var workspace = new AdhocWorkspace();
            var appId = ProjectId.CreateNewId();
            var otherId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(
                appId,
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: projectFile,
                outputFilePath: output));
            workspace.AddProject(ProjectInfo.Create(
                otherId,
                VersionStamp.Create(),
                "Other",
                "Other",
                LanguageNames.CSharp,
                filePath: otherProject));
            var solution = workspace.CurrentSolution
                .AddDocument(DocumentId.CreateNewId(appId), "Program.cs", SourceText.From("class Program {}"), filePath: program)
                .AddDocument(DocumentId.CreateNewId(appId), "Manual.cs", SourceText.From("class Manual {}"), filePath: manual)
                .AddDocument(DocumentId.CreateNewId(appId), "View.g.cs", SourceText.From("class Generated {}"), filePath: generated, isGenerated: true)
                .AddDocument(DocumentId.CreateNewId(appId), "Mixed.cs", SourceText.From("class Mixed {}"), filePath: mixed, isGenerated: true)
                .AddDocument(DocumentId.CreateNewId(otherId), "Mixed.cs", SourceText.From("class Mixed {}"), filePath: mixed)
                .AddDocument(DocumentId.CreateNewId(appId), "Shared.cs", SourceText.From("class Shared {}"), filePath: shared)
                .AddAdditionalDocument(DocumentId.CreateNewId(appId), "MainWindow.xaml", SourceText.From("<Window />"), filePath: xaml);
            if (!workspace.TryApplyChanges(solution))
            {
                throw new InvalidOperationException("Applying the classification fixture failed.");
            }

            var map = WorkspaceInputMapBuilder.Build(workspace.CurrentSolution, Guid.NewGuid(), Path.Combine(root, "App.sln"), loadGraphComplete: true);
            var manager = SolutionManagerTestFactory.Create();
            return new MapFixture(root, workspace, manager, map, projectText, appId, otherId);
        }

        public WorkspaceInputSession Publish()
        {
            var session = Manager.StartDiskWatcherSessionForTests(new[] { ProjectFile, OtherProject });
            var published = WorkspaceInputMapBuilder.Build(_workspace.CurrentSolution, session.Generation, SolutionPath, loadGraphComplete: true);
            Assert.True(session.TryPublishInputMap(published));
            return session;
        }

        public IReadOnlyList<string> SessionComposition(WorkspaceInputSession session) => session.CompositionPaths;

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
