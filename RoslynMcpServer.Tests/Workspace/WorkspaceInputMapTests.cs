using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceInputMapTests : IClassFixture<WorkspaceInputMapTests.GraphFixture>
{
    private readonly GraphFixture _fixture;

    public WorkspaceInputMapTests(GraphFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Shared_path_keeps_every_project_and_document()
    {
        var entry = Require(_fixture.SharedPath);
        Assert.Equal(2, entry.Occurrences.Count);
        Assert.Contains(entry.Occurrences, occurrence =>
            occurrence.ProjectId == _fixture.InnerId && occurrence.DocumentId == _fixture.SharedInnerDocumentId);
        Assert.Contains(entry.Occurrences, occurrence =>
            occurrence.ProjectId == _fixture.ExternalId && occurrence.DocumentId == _fixture.SharedExternalDocumentId);
    }

    [Fact]
    public void One_path_keeps_generated_producer_and_user_consumer()
    {
        var entry = Require(_fixture.MixedPath);
        Assert.Contains(entry.Occurrences, occurrence =>
            occurrence.Role == InputRole.Generated && occurrence.ProducerProjectId == _fixture.InnerId);
        Assert.Contains(entry.Occurrences, occurrence =>
            occurrence.Role == InputRole.UserInput && occurrence.ProjectId == _fixture.ExternalId);
        Assert.Contains(_fixture.InnerId, entry.Producers);
        Assert.Contains(_fixture.ExternalId, entry.Consumers);
        Assert.DoesNotContain(_fixture.InnerId, entry.Consumers);
    }

    [Fact]
    public void Generated_compile_items_are_not_user_inputs()
    {
        foreach (var path in new[] { _fixture.AssemblyInfoPath, _fixture.GeneratedSourcePath })
        {
            var entry = Require(path);
            Assert.Contains(entry.Occurrences, occurrence => occurrence.Role == InputRole.Generated);
            Assert.DoesNotContain(entry.Occurrences, occurrence => occurrence.Role == InputRole.UserInput);
            Assert.Contains(_fixture.InnerId, entry.Producers);
        }

        var unflagged = Require(_fixture.UnflaggedAssemblyInfoPath);
        Assert.Contains(unflagged.Occurrences, occurrence => occurrence.Role == InputRole.Unknown);
        Assert.DoesNotContain(unflagged.Occurrences, occurrence => occurrence.Role == InputRole.UserInput);
        Assert.DoesNotContain(unflagged.Occurrences, occurrence => occurrence.Role == InputRole.Generated);
    }

    [Fact]
    public void Explicit_user_input_inside_obj_keeps_the_user_role()
    {
        var entry = Require(_fixture.ManualPath);
        Assert.Contains(entry.Occurrences, occurrence =>
            occurrence.Role == InputRole.UserInput && occurrence.DocumentId == _fixture.ManualDocumentId);
        Assert.DoesNotContain(entry.Occurrences, occurrence => occurrence.Role == InputRole.Generated);
        Assert.True(entry.Present);
    }

    [Fact]
    public void Ambiguous_intermediate_path_is_unknown_and_not_ignored()
    {
        var consultation = _fixture.Map.Consult(_fixture.MysteryPath);
        Assert.Null(consultation.Known);
        Assert.Equal(InputRole.Unknown, consultation.UnindexedRole);
        Assert.False(consultation.Ignored);
        Assert.False(consultation.ProvenExcluded);
        Assert.NotEmpty(consultation.PotentialRegions);
    }

    [Fact]
    public void New_file_under_the_project_directory_stays_a_potential_member()
    {
        var consultation = _fixture.Map.Consult(Path.Combine(_fixture.ProjectDirectory, "Fresh.cs"));
        Assert.Null(consultation.Known);
        Assert.Equal(InputRole.PotentialMembership, consultation.UnindexedRole);
        Assert.False(consultation.ProvenExcluded);
        Assert.Contains(consultation.PotentialRegions, region =>
            region.Recursive && region.Owner == _fixture.InnerId);
    }

    [Fact]
    public void Compile_remove_is_not_a_known_input_and_not_a_proven_exclusion()
    {
        Assert.True(File.Exists(_fixture.RemovedPath));
        var consultation = _fixture.Map.Consult(_fixture.RemovedPath);
        Assert.Null(consultation.Known);
        Assert.False(consultation.ProvenExcluded);
        Assert.Equal(InputRole.PotentialMembership, consultation.UnindexedRole);
    }

    [Fact]
    public void Ancestor_props_do_not_add_a_recursive_watcher_on_the_foreign_tree()
    {
        var propsDirectory = Path.GetDirectoryName(_fixture.PropsPath)!;
        Assert.All(
            _fixture.Map.Watchers.Where(watcher => watcher.IncludeSubdirectories),
            watcher => Assert.True(
                SamePath(watcher.Directory, _fixture.ProjectDirectory)
                    || SamePath(watcher.Directory, _fixture.ExternalDirectory),
                watcher.Directory));
        Assert.DoesNotContain(
            _fixture.Map.Watchers,
            watcher => watcher.IncludeSubdirectories && SamePath(watcher.Directory, propsDirectory));
        var point = _fixture.Map.Watchers.Single(watcher =>
            !watcher.IncludeSubdirectories && SamePath(watcher.Directory, propsDirectory));
        Assert.Contains(_fixture.PropsPath, point.PointFilters);
        Assert.NotEmpty(_fixture.Map.Find(_fixture.PropsPath)!.Occurrences);
    }

    [Fact]
    public void Missing_props_file_is_an_absent_point_subscription()
    {
        var missing = Path.Combine(_fixture.ProjectDirectory, "Directory.Build.targets");
        Assert.False(File.Exists(missing));
        var entry = Require(missing);
        Assert.False(entry.Present);
        var point = _fixture.Map.Watchers.Single(watcher =>
            !watcher.IncludeSubdirectories && SamePath(watcher.Directory, _fixture.ProjectDirectory));
        Assert.Contains(missing, point.PointFilters);
        var recursive = _fixture.Map.Watchers.Single(watcher =>
            watcher.IncludeSubdirectories && SamePath(watcher.Directory, _fixture.ProjectDirectory));
        Assert.Empty(recursive.PointFilters);
    }

    [Fact]
    public void External_glob_has_no_recursive_watcher_and_unknown_coverage()
    {
        Assert.NotNull(_fixture.Map.Find(_fixture.MatchedGlobPath));
        Assert.All(
            _fixture.Map.Watchers.Where(watcher => SamePath(watcher.Directory, _fixture.GlobDirectory)),
            watcher => Assert.False(watcher.IncludeSubdirectories));
        var sibling = _fixture.Map.Consult(_fixture.UnindexedGlobPath);
        Assert.Null(sibling.Known);
        Assert.Empty(sibling.PotentialRegions);
        Assert.True(sibling.ExternalAppearanceUnknown);
        Assert.Equal(InputRole.Unknown, sibling.UnindexedRole);
        var category = _fixture.Map.Categories.Single(item => item.Kind == InputCategoryKind.ExternalGlobs);
        Assert.False(category.CompletenessKnown);
        Assert.Contains(InputCoverageReason.ExternalGlobCompletenessUnknown, _fixture.Map.UnknownReasons);
    }

    [Fact]
    public void Metadata_only_reference_is_not_complete_graph_coverage()
    {
        Assert.Empty(_fixture.Map.InstancesForFile(_fixture.ConditionalProjectPath));
        Assert.Contains(_fixture.ConditionalAssemblyPath, _fixture.Map.MetadataOnlyDependencyPaths);
        Assert.DoesNotContain(typeof(object).Assembly.Location, _fixture.Map.MetadataOnlyDependencyPaths);
        var reference = Require(_fixture.ConditionalAssemblyPath);
        Assert.Contains(reference.Occurrences, occurrence => occurrence.Role == InputRole.MetadataDependency);
        Assert.Equal(InputCoverage.Unknown, _fixture.Map.WholeGraphCoverage);
        Assert.False(_fixture.Map.PublishedAsComplete);
        Assert.Contains(InputCoverageReason.MetadataOnlyDependency, _fixture.Map.UnknownReasons);
    }

    [Fact]
    public void Failed_load_does_not_publish_the_index_as_complete()
    {
        var failed = WorkspaceInputMapBuilder.Build(
            _fixture.Solution,
            Guid.NewGuid(),
            _fixture.SolutionPath,
            loadGraphComplete: false);
        Assert.False(failed.LoadGraphComplete);
        Assert.False(failed.PublishedAsComplete);
        Assert.Equal(InputCoverage.Unknown, failed.WholeGraphCoverage);
        Assert.Contains(InputCoverageReason.LoadIncomplete, failed.UnknownReasons);
        Assert.NotNull(failed.Find(_fixture.ProgramPath));
    }

    [Fact]
    public void Single_loaded_tfm_does_not_invent_a_second_instance()
    {
        Assert.Single(_fixture.Map.InstancesForFile(_fixture.InnerProjectPath));
        Assert.Single(_fixture.Map.InstancesForFile(_fixture.ExternalProjectPath));
        Assert.Equal(InputCoverage.Complete, _fixture.Map.MultiTargetCoverage);
        Assert.DoesNotContain(InputCoverageReason.UnconfirmedTargetFramework, _fixture.Map.UnknownReasons);
        Assert.Equal(InputCoverage.Unknown, _fixture.Map.WholeGraphCoverage);
        Assert.False(_fixture.Map.PublishedAsComplete);
    }

    [Fact]
    public void Unconfirmed_other_tfm_marks_the_whole_graph_unknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpInputMapTfm-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "Multi.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="OnlyNet8.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "class Program {}");
            File.WriteAllText(Path.Combine(root, "OnlyNet8.cs"), "class OnlyNet8 {}");

            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Multi",
                "Multi",
                LanguageNames.CSharp,
                filePath: projectPath));
            var solution = workspace.CurrentSolution
                .AddDocument(DocumentId.CreateNewId(projectId), "Program.cs", SourceText.From("class Program {}"), filePath: Path.Combine(root, "Program.cs"));
            Assert.True(workspace.TryApplyChanges(solution));

            var map = WorkspaceInputMapBuilder.Build(workspace.CurrentSolution, Guid.NewGuid(), projectPath, loadGraphComplete: true);
            Assert.Single(map.InstancesForFile(projectPath));
            Assert.Equal(InputCoverage.Unknown, map.MultiTargetCoverage);
            Assert.Equal(InputCoverage.Unknown, map.WholeGraphCoverage);
            Assert.False(map.PublishedAsComplete);
            Assert.Contains(InputCoverageReason.UnconfirmedTargetFramework, map.UnknownReasons);
            Assert.Null(map.Find(Path.Combine(root, "OnlyNet8.cs"))?.Occurrences.FirstOrDefault(occurrence => occurrence.Role == InputRole.UserInput));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Search_roots_stay_the_loaded_workspace_and_project_directories()
    {
        var expected = SolutionManager.ComputeWatchRoots(
            _fixture.SolutionPath,
            [_fixture.InnerProjectPath, _fixture.ExternalProjectPath]);
        Assert.Equal(expected, _fixture.Map.SearchRoots);
        Assert.DoesNotContain(_fixture.Map.SearchRoots, root => SamePath(root, _fixture.LinkedDirectory));
        Assert.DoesNotContain(_fixture.Map.SearchRoots, root => SamePath(root, _fixture.GlobDirectory));
        Assert.DoesNotContain(_fixture.Map.SearchRoots, root => SamePath(root, Path.GetDirectoryName(_fixture.PropsPath)!));
        Assert.DoesNotContain(_fixture.Map.SearchRoots, root => SamePath(root, _fixture.RolesDirectory));
    }

    [Fact]
    public void Path_comparison_follows_the_operating_system_and_does_not_merge_distinct_paths()
    {
        var program = Require(_fixture.ProgramPath);
        var shared = Require(_fixture.SharedPath);
        Assert.NotEqual(program.CanonicalPath, shared.CanonicalPath);

        var dotted = Path.Combine(_fixture.ProjectDirectory, "obj", "..", "Program.cs");
        Assert.Equal(program.CanonicalPath, _fixture.Map.Consult(dotted).CanonicalPath);

        var flipped = Path.Combine(_fixture.ProjectDirectory, "program.cs");
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(_fixture.Map.Find(flipped));
        }
        else
        {
            Assert.Null(_fixture.Map.Find(flipped));
        }
    }

    [Fact]
    public void Every_category_records_source_events_owners_and_limits()
    {
        foreach (var kind in Enum.GetValues<InputCategoryKind>())
        {
            var category = _fixture.Map.Categories.Single(item => item.Kind == kind);
            Assert.False(string.IsNullOrWhiteSpace(category.DataSource));
            Assert.False(string.IsNullOrWhiteSpace(category.WatchedEvents));
            Assert.False(string.IsNullOrWhiteSpace(category.Limits));
        }

        Assert.Contains(
            Require(_fixture.XamlPath).Occurrences,
            occurrence => occurrence.Role == InputRole.AdditionalFile);
        Assert.Contains(
            Require(_fixture.ResourcePath).Occurrences,
            occurrence => occurrence.Role == InputRole.AdditionalFile);
        Assert.Contains(
            Require(_fixture.AnalyzerConfigPath).Occurrences,
            occurrence => occurrence.Role == InputRole.AnalyzerConfig);
        Assert.Contains(_fixture.ExternalId, _fixture.Map.Projects.Single(project => project.Id == _fixture.InnerId).ProjectReferenceIds);
        Assert.False(_fixture.Map.Categories.Single(item => item.Kind == InputCategoryKind.Imports).CompletenessKnown);
        Assert.False(_fixture.Map.Categories.Single(item => item.Kind == InputCategoryKind.RestoreInputs).CompletenessKnown);
        Assert.False(_fixture.Map.Categories.Single(item => item.Kind == InputCategoryKind.CustomTasks).CompletenessKnown);
        Assert.False(_fixture.Map.Categories.Single(item => item.Kind == InputCategoryKind.Xaml).CompletenessKnown);
    }

    [Fact]
    public void Publishing_the_map_keeps_the_session_generation()
    {
        var session = new WorkspaceInputSession([_fixture.InnerProjectPath]);
        var map = WorkspaceInputMapBuilder.Build(
            _fixture.Solution,
            session.Generation,
            _fixture.SolutionPath,
            loadGraphComplete: true);
        Assert.True(session.TryPublishInputMap(map));
        Assert.Equal(session.Generation, session.InputMap!.Generation);
        Assert.Equal(1, session.MembershipRevision);

        var foreign = WorkspaceInputMapBuilder.Build(
            _fixture.Solution,
            Guid.NewGuid(),
            _fixture.SolutionPath,
            loadGraphComplete: true);
        Assert.False(session.TryPublishInputMap(foreign));
        Assert.Equal(1, session.MembershipRevision);
        Assert.Same(map, session.InputMap);

        session.Close();
        Assert.False(session.TryPublishInputMap(map));
        Assert.Equal(1, session.MembershipRevision);
    }

    [Fact]
    public void Test_session_without_a_solution_does_not_invent_a_map()
    {
        var manager = SolutionManagerTestFactory.Create();
        var session = manager.StartDiskWatcherSessionForTests([_fixture.InnerProjectPath]);
        Assert.Null(session.InputMap);
        Assert.Equal(0, session.MembershipRevision);
        Assert.NotEqual(Guid.Empty, session.Generation);
    }

    [Fact]
    public async Task Real_inner_tfm_load_keeps_one_instance_and_does_not_publish_complete_coverage()
    {
        MsBuildBootstrapper.Register();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpInputMapLoad-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="OnlyNet8.cs" Link="OnlyNet8.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "namespace App; public sealed class Program { }");
            File.WriteAllText(Path.Combine(root, "OnlyNet8.cs"), "namespace App; public sealed class OnlyNet8 { }");

            var manager = SolutionManagerTestFactory.Create();
            await manager.LoadAsync(projectPath, CancellationToken.None, targetFramework: "net10.0");
            var session = manager.DiskWatcherSession;
            Assert.NotNull(session);
            var map = session.InputMap;
            Assert.NotNull(map);
            Assert.Equal(session.Generation, map.Generation);
            Assert.True(session.MembershipRevision >= 1);

            var instances = map.InstancesForFile(projectPath);
            var described = string.Join(
                "; ",
                map.Paths.Select(entry =>
                    Path.GetFileName(entry.CanonicalPath)
                    + "="
                    + string.Join(",", entry.Occurrences.Select(occurrence => occurrence.Role))));
            Assert.True(
                instances.Count == 1,
                "Real load produced " + instances.Count + " project instances. " + described);
            Assert.Equal(InputCoverage.Unknown, map.MultiTargetCoverage);
            Assert.Equal(InputCoverage.Unknown, map.WholeGraphCoverage);
            Assert.False(map.PublishedAsComplete);
            Assert.Contains(InputCoverageReason.UnconfirmedTargetFramework, map.UnknownReasons);
            Assert.Contains(
                Require(map, Path.Combine(root, "Program.cs")).Occurrences,
                occurrence => occurrence.Role == InputRole.UserInput);
            // The SDK glob still compiles this file for the loaded TFM. The other TFM stays
            // unconfirmed because the project declares two frameworks and the graph has one instance.
            Assert.Contains(
                Require(map, Path.Combine(root, "OnlyNet8.cs")).Occurrences,
                occurrence => occurrence.Role == InputRole.UserInput);
            Assert.Contains(
                map.Paths,
                entry => entry.CanonicalPath.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
                    && entry.Occurrences.All(occurrence => occurrence.Role == InputRole.Unknown));
            Assert.Contains(
                map.Paths,
                entry => entry.CanonicalPath.EndsWith("AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase)
                    && entry.Occurrences.All(occurrence => occurrence.Role == InputRole.Unknown));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private InputPathEntry Require(string path) => Require(_fixture.Map, path);

    private static InputPathEntry Require(WorkspaceInputMap map, string path)
    {
        var entry = map.Find(path);
        Assert.NotNull(entry);
        return entry;
    }

    private static bool SamePath(string left, string right)
        => InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

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
            // The real load test leaves a best-effort cleanup if MSBuild still holds a file.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public sealed class GraphFixture : IDisposable
    {
        public string WalkRoot { get; }

        public string ProjectDirectory { get; }

        public string SolutionDirectory { get; }

        public string ExternalDirectory { get; }

        public string LinkedDirectory { get; }

        public string GlobDirectory { get; }

        public string RolesDirectory { get; }

        public string ConditionalDirectory { get; }

        public string PropsPath { get; }

        public string InnerProjectPath { get; }

        public string ExternalProjectPath { get; }

        public string SolutionPath { get; }

        public string ConditionalProjectPath { get; }

        public string ConditionalAssemblyPath { get; }

        public string SharedPath { get; }

        public string MixedPath { get; }

        public string ProgramPath { get; }

        public string RemovedPath { get; }

        public string ManualPath { get; }

        public string AssemblyInfoPath { get; }

        public string UnflaggedAssemblyInfoPath { get; }

        public string GeneratedSourcePath { get; }

        public string MysteryPath { get; }

        public string XamlPath { get; }

        public string ResourcePath { get; }

        public string AnalyzerConfigPath { get; }

        public string MatchedGlobPath { get; }

        public string UnindexedGlobPath { get; }

        public ProjectId InnerId { get; }

        public ProjectId ExternalId { get; }

        public DocumentId SharedInnerDocumentId { get; }

        public DocumentId SharedExternalDocumentId { get; }

        public DocumentId ManualDocumentId { get; }

        public Solution Solution { get; }

        internal WorkspaceInputMap Map { get; }

        public GraphFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "RoslynMcpInputMap-" + Guid.NewGuid().ToString("N"));
            WalkRoot = Path.Combine(_root, "walk");
            ProjectDirectory = Path.Combine(WalkRoot, "mid", "low", "app");
            SolutionDirectory = Path.Combine(_root, "solution-home");
            ExternalDirectory = Path.Combine(_root, "external");
            LinkedDirectory = Path.Combine(_root, "linked");
            GlobDirectory = Path.Combine(_root, "external-glob");
            RolesDirectory = Path.Combine(_root, "roles");
            ConditionalDirectory = Path.Combine(_root, "conditional");

            PropsPath = Path.Combine(WalkRoot, "Directory.Build.props");
            InnerProjectPath = Path.Combine(ProjectDirectory, "Inner.csproj");
            ExternalProjectPath = Path.Combine(ExternalDirectory, "External.csproj");
            SolutionPath = Path.Combine(SolutionDirectory, "App.sln");
            ConditionalProjectPath = Path.Combine(ConditionalDirectory, "Conditional.csproj");
            ConditionalAssemblyPath = Path.Combine(ConditionalDirectory, "Conditional.dll");
            SharedPath = Path.Combine(LinkedDirectory, "Shared.cs");
            MixedPath = Path.Combine(RolesDirectory, "Mixed.cs");
            ProgramPath = Path.Combine(ProjectDirectory, "Program.cs");
            RemovedPath = Path.Combine(ProjectDirectory, "Removed.cs");
            ManualPath = Path.Combine(ProjectDirectory, "obj", "Manual.cs");
            AssemblyInfoPath = Path.Combine(ProjectDirectory, "obj", "Inner.AssemblyInfo.cs");
            UnflaggedAssemblyInfoPath = Path.Combine(ProjectDirectory, "obj", "Unflagged.AssemblyInfo.cs");
            GeneratedSourcePath = Path.Combine(ProjectDirectory, "obj", "View.g.cs");
            MysteryPath = Path.Combine(ProjectDirectory, "obj", "Mystery.g.cs");
            XamlPath = Path.Combine(ProjectDirectory, "MainWindow.xaml");
            ResourcePath = Path.Combine(ProjectDirectory, "Strings.resx");
            AnalyzerConfigPath = Path.Combine(ProjectDirectory, ".editorconfig");
            MatchedGlobPath = Path.Combine(GlobDirectory, "Matched.cs");
            UnindexedGlobPath = Path.Combine(GlobDirectory, "NotInGraph.cs");

            Directory.CreateDirectory(ProjectDirectory);
            Directory.CreateDirectory(Path.Combine(ProjectDirectory, "obj"));
            Directory.CreateDirectory(SolutionDirectory);
            Directory.CreateDirectory(ExternalDirectory);
            Directory.CreateDirectory(LinkedDirectory);
            Directory.CreateDirectory(GlobDirectory);
            Directory.CreateDirectory(RolesDirectory);
            Directory.CreateDirectory(ConditionalDirectory);

            File.WriteAllText(
                PropsPath,
                """
                <Project>
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(InnerProjectPath, InnerProjectText());
            File.WriteAllText(
                ExternalProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(SolutionPath, "Microsoft Visual Studio Solution File, Format Version 12.00");
            File.WriteAllText(
                ConditionalProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.Copy(typeof(WorkspaceInputMapTests).Assembly.Location, ConditionalAssemblyPath, overwrite: true);
            File.WriteAllText(SharedPath, "class Shared {}");
            File.WriteAllText(MixedPath, "class Mixed {}");
            File.WriteAllText(ProgramPath, "class Program {}");
            File.WriteAllText(RemovedPath, "class Removed {}");
            File.WriteAllText(ManualPath, "class Manual {}");
            File.WriteAllText(AssemblyInfoPath, "// generated assembly info");
            File.WriteAllText(UnflaggedAssemblyInfoPath, "// flag clear");
            File.WriteAllText(GeneratedSourcePath, "// generated");
            File.WriteAllText(MysteryPath, "// ambiguous");
            File.WriteAllText(XamlPath, "<Window />");
            File.WriteAllText(ResourcePath, "<root />");
            File.WriteAllText(AnalyzerConfigPath, "root = true");
            File.WriteAllText(MatchedGlobPath, "class Matched {}");
            File.WriteAllText(UnindexedGlobPath, "class NotInGraph {}");
            File.WriteAllText(Path.Combine(ExternalDirectory, "Local.cs"), "class Local {}");

            InnerId = ProjectId.CreateNewId();
            ExternalId = ProjectId.CreateNewId();
            SharedInnerDocumentId = DocumentId.CreateNewId(InnerId);
            SharedExternalDocumentId = DocumentId.CreateNewId(ExternalId);
            ManualDocumentId = DocumentId.CreateNewId(InnerId);

            var outputPath = Path.Combine(ProjectDirectory, "bin", "Debug", "net10.0", "Inner.dll");
            _workspace.AddProject(ProjectInfo.Create(
                InnerId,
                VersionStamp.Create(),
                "Inner",
                "Inner",
                LanguageNames.CSharp,
                filePath: InnerProjectPath,
                outputFilePath: outputPath));
            _workspace.AddProject(ProjectInfo.Create(
                ExternalId,
                VersionStamp.Create(),
                "External",
                "External",
                LanguageNames.CSharp,
                filePath: ExternalProjectPath));

            var solution = _workspace.CurrentSolution
                .AddProjectReference(InnerId, new ProjectReference(ExternalId))
                .AddMetadataReference(InnerId, MetadataReference.CreateFromFile(ConditionalAssemblyPath))
                .AddMetadataReference(InnerId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            solution = AddSource(solution, SharedInnerDocumentId, "Shared.cs", SharedPath, isGenerated: false);
            solution = AddSource(solution, SharedExternalDocumentId, "Shared.cs", SharedPath, isGenerated: false);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "Mixed.cs", MixedPath, isGenerated: true);
            solution = AddSource(solution, DocumentId.CreateNewId(ExternalId), "Mixed.cs", MixedPath, isGenerated: false);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "Program.cs", ProgramPath, isGenerated: false);
            solution = AddSource(solution, ManualDocumentId, "Manual.cs", ManualPath, isGenerated: false);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "Inner.AssemblyInfo.cs", AssemblyInfoPath, isGenerated: true);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "Unflagged.AssemblyInfo.cs", UnflaggedAssemblyInfoPath, isGenerated: false);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "View.g.cs", GeneratedSourcePath, isGenerated: true);
            solution = AddSource(solution, DocumentId.CreateNewId(InnerId), "Matched.cs", MatchedGlobPath, isGenerated: false);
            solution = AddSource(solution, DocumentId.CreateNewId(ExternalId), "Local.cs", Path.Combine(ExternalDirectory, "Local.cs"), isGenerated: false);
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(InnerId), "MainWindow.xaml", "<Window />", filePath: XamlPath);
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(InnerId), "Strings.resx", "<root />", filePath: ResourcePath);
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(InnerId), "Extra.txt", "extra", filePath: Path.Combine(ProjectDirectory, "Extra.txt"));

            if (!_workspace.TryApplyChanges(solution))
            {
                throw new InvalidOperationException("Applying the input-map fixture solution failed.");
            }

            var analyzer = _workspace.CurrentSolution.GetProject(InnerId)!
                .AddAnalyzerConfigDocument(".editorconfig", SourceText.From("root = true"), filePath: AnalyzerConfigPath);
            if (!_workspace.TryApplyChanges(analyzer.Project.Solution))
            {
                throw new InvalidOperationException("Applying the analyzer config document failed.");
            }

            Solution = _workspace.CurrentSolution;
            Map = WorkspaceInputMapBuilder.Build(Solution, Guid.NewGuid(), SolutionPath, loadGraphComplete: true);
        }

        public void Dispose()
        {
            _workspace.Dispose();
            TryDelete(_root);
        }

        private static Solution AddSource(Solution solution, DocumentId documentId, string name, string path, bool isGenerated)
        {
            return solution.AddDocument(documentId, name, SourceText.From("class Item {}"), filePath: path, isGenerated: isGenerated);
        }

        private string InnerProjectText()
        {
            var conditional = Path.GetRelativePath(ProjectDirectory, ConditionalProjectPath);
            var glob = Path.GetRelativePath(ProjectDirectory, GlobDirectory);
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{glob}\**\*.cs" />
                    <Compile Remove="Removed.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <ProjectReference Include="{conditional}" />
                  </ItemGroup>
                </Project>
                """;
        }

        private readonly string _root;
        private readonly AdhocWorkspace _workspace = new();
    }
}
