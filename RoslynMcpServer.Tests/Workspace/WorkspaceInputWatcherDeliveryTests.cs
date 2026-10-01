using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceInputWatcherDeliveryTests : IClassFixture<WorkspaceInputWatcherDeliveryTests.DeliveryFixture>
{
    private readonly DeliveryFixture _fixture;

    public WorkspaceInputWatcherDeliveryTests(DeliveryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Real_watcher_updates_every_project_that_links_an_external_file()
    {
        var session = _fixture.Session;
        var beforeApp = session.InputRevisionFor(_fixture.AppId);
        var beforeOther = session.InputRevisionFor(_fixture.OtherId);
        var updated = """
            public class SharedFile
            {
                public int CommonAfter;
            #if APP
                public int AppOnly;
            #endif
            #if OTHER
                public int OtherOnly;
            #endif
            }
            """;
        await File.WriteAllTextAsync(_fixture.SharedPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var delivered = await _fixture.Manager.WaitForDirtySourceAsync(_fixture.SharedPath, TimeSpan.FromSeconds(8));
        Assert.True(delivered.Delivered, "FileSystemWatcher did not deliver the linked file outside every project directory.");

        var solution = await _fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        var documents = solution.Projects
            .SelectMany(project => project.Documents)
            .Where(document => Same(document.FilePath, _fixture.SharedPath))
            .ToArray();
        Assert.Equal(2, documents.Length);
        string? sharedText = null;
        foreach (var document in documents)
        {
            var text = (await document.GetTextAsync()).ToString();
            sharedText ??= text;
            Assert.Equal(sharedText, text);
            Assert.Contains("CommonAfter", text, StringComparison.Ordinal);
        }

        var disk = await File.ReadAllTextAsync(_fixture.SharedPath);
        Assert.Equal(sharedText, disk);
        Assert.Equal(_fixture.AppProjectBytes, await File.ReadAllBytesAsync(_fixture.AppProjectPath));
        Assert.Equal(_fixture.OtherProjectBytes, await File.ReadAllBytesAsync(_fixture.OtherProjectPath));
        Assert.Equal(_fixture.SolutionBytes, await File.ReadAllBytesAsync(_fixture.SolutionPath));

        var appCompilation = await solution.Projects.Single(project => project.Id == _fixture.AppId).GetCompilationAsync();
        var otherCompilation = await solution.Projects.Single(project => project.Id == _fixture.OtherId).GetCompilationAsync();
        var appType = appCompilation!.GetTypeByMetadataName("SharedFile");
        var otherType = otherCompilation!.GetTypeByMetadataName("SharedFile");
        Assert.NotNull(appType);
        Assert.NotNull(otherType);
        Assert.NotEmpty(appType.GetMembers("AppOnly"));
        Assert.Empty(appType.GetMembers("OtherOnly"));
        Assert.NotEmpty(otherType.GetMembers("OtherOnly"));
        Assert.Empty(otherType.GetMembers("AppOnly"));
        Assert.True(session.InputRevisionFor(_fixture.AppId) > beforeApp);
        Assert.True(session.InputRevisionFor(_fixture.OtherId) > beforeOther);
    }

    [Fact]
    public async Task Glob_add_exclusion_delete_and_rename_do_not_write_the_project_file()
    {
        var session = _fixture.Session;
        var beforeCount = (await PublishedDocuments()).Count();
        await File.WriteAllTextAsync(_fixture.FreshPath, "public class FreshArrival {}");
        await File.AppendAllTextAsync(_fixture.RemovedPath, " ");
        await File.AppendAllTextAsync(_fixture.DeletePath, " ");
        File.Delete(_fixture.DeletePath);

        Assert.True(await WaitUntil(() => Contains(session.CompositionPaths, _fixture.FreshPath)));
        Assert.True(await WaitUntil(() => Contains(session.CompositionPaths, _fixture.RemovedPath)));
        Assert.True(await WaitUntil(() => Contains(session.CompositionPaths, _fixture.DeletePath)));
        Assert.False(_fixture.Map.Consult(_fixture.RemovedPath).ProvenExcluded);

        var renamed = Path.Combine(_fixture.AppDirectory, "RenamedAway.cs");
        File.Move(_fixture.RenamePath, renamed);
        Assert.True(
            await WaitUntil(() =>
                Contains(session.CompositionPaths, _fixture.RenamePath)
                || Contains(session.CompositionPaths, renamed)
                || Contains(session.DirectoryRenamePaths, _fixture.RenamePath)),
            "Neither side of the file rename was recorded.");

        var solution = await _fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        Assert.Equal(beforeCount, solution.Projects.SelectMany(project => project.Documents).Count());
        Assert.DoesNotContain(
            solution.Projects.SelectMany(project => project.Documents),
            document => Same(document.FilePath, _fixture.FreshPath));
        Assert.Equal(_fixture.AppProjectBytes, await File.ReadAllBytesAsync(_fixture.AppProjectPath));
        Assert.Equal(_fixture.SolutionBytes, await File.ReadAllBytesAsync(_fixture.SolutionPath));
    }

    [Fact]
    public async Task Non_recursive_watchers_see_linked_and_props_but_not_a_foreign_nested_tree()
    {
        var session = _fixture.Session;
        Assert.Contains(_fixture.Manager.DiskWatcherSubscriptions, subscription =>
            Same(subscription.Directory, _fixture.LinkedDirectory) && !subscription.IncludeSubdirectories);
        Assert.Contains(_fixture.Manager.DiskWatcherSubscriptions, subscription =>
            Same(subscription.Directory, _fixture.WalkDirectory) && !subscription.IncludeSubdirectories);
        Assert.Contains(_fixture.Manager.DiskWatcherSubscriptions, subscription =>
            Same(subscription.Directory, _fixture.AppDirectory) && subscription.IncludeSubdirectories);
        Assert.DoesNotContain(_fixture.Manager.DiskWatcherSubscriptions, subscription =>
            subscription.IncludeSubdirectories && Same(subscription.Directory, _fixture.WalkDirectory));
        Assert.DoesNotContain(_fixture.Manager.DiskWatcherSubscriptions, subscription =>
            subscription.IncludeSubdirectories
            && !_fixture.ProjectDirectories.Any(directory => Same(directory, subscription.Directory)));

        var beforeProps = session.InputRevisionFor(_fixture.AppId);
        await File.AppendAllTextAsync(_fixture.PropsPath, Environment.NewLine + "<!-- touch -->");
        Assert.True(await WaitUntil(() => session.InputRevisionFor(_fixture.AppId) > beforeProps), "Walk-up props change was not delivered.");

        var targetsPath = Path.Combine(_fixture.WalkDirectory, "Directory.Build.targets");
        Assert.False(File.Exists(targetsPath));
        var beforeTargets = session.InputRevisionFor(_fixture.AppId);
        await File.WriteAllTextAsync(targetsPath, "<Project />");
        Assert.True(
            await WaitUntil(() => session.InputRevisionFor(_fixture.AppId) > beforeTargets),
            "Appearance of the absent props file was not delivered.");

        var foreign = Path.Combine(_fixture.WalkDirectory, "mid", "foreign", "Foreign.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        var noticesBefore = session.Notices.Count(notice => Same(notice.CanonicalPath, foreign));
        await File.WriteAllTextAsync(foreign, "class Foreign {}");
        await Task.Delay(1500);
        Assert.Equal(noticesBefore, session.Notices.Count(notice => Same(notice.CanonicalPath, foreign)));
        Assert.DoesNotContain(_fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, foreign));

        var sibling = Path.Combine(_fixture.GlobDirectory, "NotInGraph.cs");
        await File.WriteAllTextAsync(sibling, "class NotInGraph {}");
        Assert.True(await WaitUntil(() => session.Notices.Any(notice =>
            Same(notice.CanonicalPath, sibling) && notice.Role == InputRole.Unknown)));
        Assert.Contains(InputCoverageReason.ExternalGlobCompletenessUnknown, session.ObservationUnknownReasons);
        Assert.DoesNotContain(session.CompositionPaths, path => Same(path, sibling));
    }

    [Fact]
    public async Task Directory_rename_inside_a_membership_region_stays_unknown_after_re_read()
    {
        var session = _fixture.Session;
        var area = Path.Combine(_fixture.AppDirectory, "Area" + Guid.NewGuid().ToString("N"));
        var moved = area + "Moved";
        Directory.CreateDirectory(area);
        var inside = Path.Combine(area, "Inside.cs");
        await File.WriteAllTextAsync(inside, "class Inside {}");
        await File.WriteAllTextAsync(_fixture.ProgramPath, "public class Program { public int AfterRenameRead; }", new UTF8Encoding(false));

        Directory.Move(area, moved);
        Assert.True(
            await WaitUntil(() => session.ObservationUnknownReasons.Contains(InputCoverageReason.DirectoryRenameInsideMembership)),
            "Directory rename did not mark coverage unknown.");

        var solution = await _fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        var program = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(document => Same(document.FilePath, _fixture.ProgramPath));
        Assert.Contains("AfterRenameRead", (await program.GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership, session.ObservationUnknownReasons);
        Assert.Equal(InputCoverage.Unknown, session.ObservationCoverage);
    }

    [Fact]
    public async Task Explicit_obj_input_is_synced_and_generated_outputs_do_not_bump_input_revisions()
    {
        var session = _fixture.Session;
        var before = session.InputRevisionFor(_fixture.AppId);
        await File.WriteAllTextAsync(_fixture.ManualPath, "public class ManualInput { public int FromDisk; }");
        var delivered = await _fixture.Manager.WaitForDirtySourceAsync(_fixture.ManualPath, TimeSpan.FromSeconds(8));
        Assert.True(delivered.Delivered, "Explicit input under obj was not delivered.");
        Assert.True(session.InputRevisionFor(_fixture.AppId) > before);

        var solution = await _fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        var manual = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(document => Same(document.FilePath, _fixture.ManualPath));
        Assert.Contains("FromDisk", (await manual.GetTextAsync()).ToString(), StringComparison.Ordinal);

        var afterManual = session.InputRevisionFor(_fixture.AppId);
        await File.AppendAllTextAsync(_fixture.AssemblyInfoPath, Environment.NewLine + "// again");
        await File.AppendAllTextAsync(_fixture.AssemblyInfoPath, Environment.NewLine + "// loop");
        if (!string.IsNullOrWhiteSpace(_fixture.OutputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fixture.OutputPath)!);
            await File.WriteAllTextAsync(_fixture.OutputPath, "output-bytes");
        }

        await File.WriteAllTextAsync(_fixture.MysteryPath, "class Mystery {}");
        Assert.True(
            await WaitUntil(() => session.Notices.Any(notice =>
                Same(notice.CanonicalPath, _fixture.AssemblyInfoPath) && !notice.CountsAsInputRevision)),
            "AssemblyInfo was ignored instead of recorded as a non-input role.");
        Assert.True(
            await WaitUntil(() => session.Notices.Any(notice =>
                Same(notice.CanonicalPath, _fixture.MysteryPath) && notice.Role == InputRole.Unknown)),
            "Unknown provenance under obj was ignored.");
        Assert.Equal(afterManual, session.InputRevisionFor(_fixture.AppId));
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, _fixture.AssemblyInfoPath) && !notice.CountsAsInputRevision);
        Assert.Contains(session.Notices, notice =>
            Same(notice.CanonicalPath, _fixture.MysteryPath)
            && notice.Role == InputRole.Unknown
            && !notice.CountsAsInputRevision);
        if (!string.IsNullOrWhiteSpace(_fixture.OutputPath))
        {
            Assert.Contains(session.Notices, notice =>
                Same(notice.CanonicalPath, _fixture.OutputPath)
                && notice.Role == InputRole.Output
                && !notice.CountsAsInputRevision);
        }

        Assert.DoesNotContain(_fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, _fixture.AssemblyInfoPath));
        Assert.DoesNotContain(session.CompositionPaths, path => Same(path, _fixture.MysteryPath));
    }

    [Fact]
    public async Task Search_roots_ignore_the_linked_directory_and_its_loose_neighbor()
    {
        var session = _fixture.Session;
        var expected = SolutionManager.ComputeWatchRoots(
            _fixture.SolutionPath,
            new[] { _fixture.AppProjectPath, _fixture.OtherProjectPath });
        Assert.Equal(expected, _fixture.Map.SearchRoots);
        Assert.Equal(
            expected,
            UtilityTools.ResolveSearchRoots(null, _fixture.SolutionPath, new[] { _fixture.AppProjectPath, _fixture.OtherProjectPath }, _fixture.Root));
        var explicitRoot = UtilityTools.ResolveSearchRoots(
            _fixture.AppDirectory,
            _fixture.SolutionPath,
            new[] { _fixture.AppProjectPath },
            _fixture.Root);
        Assert.Equal(new[] { Path.GetFullPath(_fixture.AppDirectory) }, explicitRoot);
        var fallback = UtilityTools.ResolveSearchRoots(null, null, null, _fixture.Root);
        Assert.Equal(new[] { Path.GetFullPath(_fixture.Root) }, fallback);
        Assert.DoesNotContain(_fixture.Map.SearchRoots, root => Same(root, _fixture.LinkedDirectory));

        var loose = Path.Combine(_fixture.LinkedDirectory, "Loose.cs");
        await File.AppendAllTextAsync(loose, Environment.NewLine + "// loose");
        Assert.True(await WaitUntil(() => session.Notices.Any(notice =>
            Same(notice.CanonicalPath, loose) && notice.Role == InputRole.Unknown)));
        Assert.DoesNotContain(session.CompositionPaths, path => Same(path, loose));
        Assert.Equal(expected, _fixture.Map.SearchRoots);
    }

    [Fact]
    public async Task Semantic_sync_preserves_bom_and_project_files()
    {
        await File.WriteAllTextAsync(_fixture.BomPath, "public class BomFile { public int After; }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await File.WriteAllTextAsync(_fixture.ProgramPath, "public class Program { public int BomFreeAfter; }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var bomDelivered = await _fixture.Manager.WaitForDirtySourceAsync(_fixture.BomPath, TimeSpan.FromSeconds(8));
        var programDelivered = await _fixture.Manager.WaitForDirtySourceAsync(_fixture.ProgramPath, TimeSpan.FromSeconds(8));
        Assert.True(bomDelivered.Delivered, "FileSystemWatcher did not deliver BomFile.cs.");
        Assert.True(programDelivered.Delivered, "FileSystemWatcher did not deliver Program.cs.");

        var solution = await _fixture.Manager.GetPublishedSolutionAfterDiskSyncAsync();
        Assert.NotNull(solution);
        var bom = await File.ReadAllBytesAsync(_fixture.BomPath);
        var program = await File.ReadAllBytesAsync(_fixture.ProgramPath);
        Assert.True(bom.Length >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF);
        Assert.False(program.Length >= 3 && program[0] == 0xEF && program[1] == 0xBB && program[2] == 0xBF);
        Assert.Equal(_fixture.AppProjectBytes, await File.ReadAllBytesAsync(_fixture.AppProjectPath));
        Assert.Equal(_fixture.SolutionBytes, await File.ReadAllBytesAsync(_fixture.SolutionPath));
        Assert.Contains(
            "BomFreeAfter",
            (await solution.Projects.SelectMany(project => project.Documents).Single(document => Same(document.FilePath, _fixture.ProgramPath)).GetTextAsync()).ToString(),
            StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<Document>> PublishedDocuments()
    {
        var solution = await _fixture.Manager.GetPublishedSolutionAsync();
        Assert.NotNull(solution);
        return solution.Projects.SelectMany(project => project.Documents).ToArray();
    }

    private static bool Contains(IReadOnlyList<string> paths, string candidate)
    {
        return paths.Any(path => Same(path, candidate));
    }

    private static async Task<bool> WaitUntil(Func<bool> ready)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
        {
            if (ready())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return ready();
    }

    private static bool Same(string? left, string? right)
    {
        return left is not null
            && right is not null
            && InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    }

    public sealed class DeliveryFixture : IAsyncLifetime
    {
        private string _root = "";

        public SolutionManager Manager { get; private set; } = null!;

        internal WorkspaceInputSession Session => Manager.DiskWatcherSession!;

        internal WorkspaceInputMap Map => Session.InputMap!;

        public string Root => _root;

        public string WalkDirectory { get; private set; } = "";

        public string AppDirectory { get; private set; } = "";

        public string LinkedDirectory { get; private set; } = "";

        public string GlobDirectory { get; private set; } = "";

        public string SolutionPath { get; private set; } = "";

        public string AppProjectPath { get; private set; } = "";

        public string OtherProjectPath { get; private set; } = "";

        public string SharedPath { get; private set; } = "";

        public string PropsPath { get; private set; } = "";

        public string ProgramPath { get; private set; } = "";

        public string BomPath { get; private set; } = "";

        public string FreshPath { get; private set; } = "";

        public string RemovedPath { get; private set; } = "";

        public string DeletePath { get; private set; } = "";

        public string RenamePath { get; private set; } = "";

        public string ManualPath { get; private set; } = "";

        public string MysteryPath { get; private set; } = "";

        public string AssemblyInfoPath { get; private set; } = "";

        public string? OutputPath { get; private set; }

        public ProjectId AppId { get; private set; } = null!;

        public ProjectId OtherId { get; private set; } = null!;

        public byte[] AppProjectBytes { get; private set; } = Array.Empty<byte>();

        public byte[] OtherProjectBytes { get; private set; } = Array.Empty<byte>();

        public byte[] SolutionBytes { get; private set; } = Array.Empty<byte>();

        public IReadOnlyList<string> ProjectDirectories { get; private set; } = Array.Empty<string>();

        public async Task InitializeAsync()
        {
            MsBuildBootstrapper.Register();
            _root = Path.Combine(Path.GetTempPath(), "RoslynMcpInputWatch-" + Guid.NewGuid().ToString("N"));
            WalkDirectory = Path.Combine(_root, "walk");
            AppDirectory = Path.Combine(WalkDirectory, "mid", "low", "app");
            var otherDirectory = Path.Combine(_root, "other");
            LinkedDirectory = Path.Combine(_root, "linked");
            GlobDirectory = Path.Combine(_root, "external-glob");
            var solutionDirectory = Path.Combine(_root, "sln-home");
            Directory.CreateDirectory(AppDirectory);
            Directory.CreateDirectory(Path.Combine(AppDirectory, "obj"));
            Directory.CreateDirectory(otherDirectory);
            Directory.CreateDirectory(LinkedDirectory);
            Directory.CreateDirectory(GlobDirectory);
            Directory.CreateDirectory(solutionDirectory);

            PropsPath = Path.Combine(WalkDirectory, "Directory.Build.props");
            AppProjectPath = Path.Combine(AppDirectory, "App.csproj");
            OtherProjectPath = Path.Combine(otherDirectory, "Other.csproj");
            SolutionPath = Path.Combine(solutionDirectory, "App.sln");
            SharedPath = Path.Combine(LinkedDirectory, "Shared.cs");
            ProgramPath = Path.Combine(AppDirectory, "Program.cs");
            BomPath = Path.Combine(AppDirectory, "BomFile.cs");
            FreshPath = Path.Combine(AppDirectory, "FreshArrival.cs");
            RemovedPath = Path.Combine(AppDirectory, "Removed.cs");
            DeletePath = Path.Combine(AppDirectory, "DeleteMe.cs");
            RenamePath = Path.Combine(AppDirectory, "RenameMe.cs");
            ManualPath = Path.Combine(AppDirectory, "obj", "Manual.cs");
            MysteryPath = Path.Combine(AppDirectory, "obj", "Mystery.g.cs");

            await File.WriteAllTextAsync(
                PropsPath,
                """
                <Project>
                  <PropertyGroup>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(AppProjectPath, AppProject());
            await File.WriteAllTextAsync(OtherProjectPath, OtherProject());
            await File.WriteAllTextAsync(SolutionPath, SolutionText());
            await File.WriteAllTextAsync(SharedPath, SharedSource(), new UTF8Encoding(false));
            await File.WriteAllTextAsync(ProgramPath, "public class Program { public int Before; }", new UTF8Encoding(false));
            await File.WriteAllTextAsync(BomPath, "public class BomFile { public int Before; }", new UTF8Encoding(true));
            await File.WriteAllTextAsync(RemovedPath, "public class RemovedMarker {}");
            await File.WriteAllTextAsync(DeletePath, "public class DeleteMe {}");
            await File.WriteAllTextAsync(RenamePath, "public class RenameMe {}");
            await File.WriteAllTextAsync(ManualPath, "public class ManualInput { public int Before; }");
            await File.WriteAllTextAsync(Path.Combine(GlobDirectory, "Matched.cs"), "public class Matched {}");
            await File.WriteAllTextAsync(Path.Combine(LinkedDirectory, "Loose.cs"), "class Loose {}");
            await File.WriteAllTextAsync(Path.Combine(otherDirectory, "Local.cs"), "public class Local {}");

            Manager = SolutionManagerTestFactory.Create();
            await Manager.LoadAsync(SolutionPath, CancellationToken.None, targetFramework: "net10.0");
            Assert.NotNull(Manager.DiskWatcherSession);
            Assert.NotNull(Manager.DiskWatcherSession.InputMap);

            var solution = await Manager.GetPublishedSolutionAsync();
            Assert.NotNull(solution);
            AppId = solution.Projects.Single(project => Same(project.FilePath, AppProjectPath)).Id;
            OtherId = solution.Projects.Single(project => Same(project.FilePath, OtherProjectPath)).Id;
            ProjectDirectories = solution.Projects
                .Select(project => Path.GetDirectoryName(project.FilePath))
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => directory!)
                .ToArray();
            AssemblyInfoPath = Map.Paths
                .Select(entry => entry.CanonicalPath)
                .First(path => path.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase));
            OutputPath = Map.Paths
                .FirstOrDefault(entry => entry.Occurrences.Any(occurrence => occurrence.Role == InputRole.Output && occurrence.ProjectId == AppId))
                ?.CanonicalPath;
            var manual = Map.Find(ManualPath);
            Assert.True(manual is not null && manual.Occurrences.Any(occurrence => occurrence.Role == InputRole.UserInput), Describe("Manual.cs"));
            Assert.Null(Map.Find(RemovedPath)?.Occurrences.FirstOrDefault(occurrence => occurrence.Role == InputRole.UserInput));
            Assert.NotNull(Map.Find(SharedPath));
            Assert.Equal(2, Map.Find(SharedPath)!.Occurrences.Count(occurrence => occurrence.Role == InputRole.UserInput));

            AppProjectBytes = await File.ReadAllBytesAsync(AppProjectPath);
            OtherProjectBytes = await File.ReadAllBytesAsync(OtherProjectPath);
            SolutionBytes = await File.ReadAllBytesAsync(SolutionPath);
        }

        public async Task DisposeAsync()
        {
            if (Manager is not null)
            {
                await Manager.ClearWorkspaceAsync();
            }

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

        private string Describe(string caption)
        {
            return caption + ": " + string.Join(
                "; ",
                Map.Paths.Select(entry =>
                    Path.GetFileName(entry.CanonicalPath)
                    + "="
                    + string.Join(",", entry.Occurrences.Select(occurrence => occurrence.Role))));
        }

        private string AppProject()
        {
            var shared = Path.GetRelativePath(AppDirectory, SharedPath);
            var glob = Path.GetRelativePath(AppDirectory, GlobDirectory);
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                    <DefineConstants>APP</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{shared}" Link="Shared.cs" />
                    <Compile Include="{glob}\*.cs" />
                    <Compile Include="obj\Manual.cs" />
                    <Compile Remove="Removed.cs" />
                  </ItemGroup>
                </Project>
                """;
        }

        private string OtherProject()
        {
            var shared = Path.GetRelativePath(Path.GetDirectoryName(OtherProjectPath)!, SharedPath);
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                    <DefineConstants>OTHER</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{shared}" Link="Shared.cs" />
                  </ItemGroup>
                </Project>
                """;
        }

        private static string SharedSource()
        {
            return """
                public class SharedFile
                {
                    public int Common;
                #if APP
                    public int AppOnly;
                #endif
                #if OTHER
                    public int OtherOnly;
                #endif
                }
                """;
        }

        private string SolutionText()
        {
            var app = Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, AppProjectPath);
            var other = Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, OtherProjectPath);
            return """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "App", "APP_PATH", "{11111111-1111-1111-1111-111111111111}"
                EndProject
                Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Other", "OTHER_PATH", "{22222222-2222-2222-2222-222222222222}"
                EndProject
                Global
                	GlobalSection(SolutionConfigurationPlatforms) = preSolution
                		Debug|Any CPU = Debug|Any CPU
                	EndGlobalSection
                	GlobalSection(ProjectConfigurationPlatforms) = postSolution
                		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
                		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
                	EndGlobalSection
                EndGlobal
                """
                .Replace("APP_PATH", app, StringComparison.Ordinal)
                .Replace("OTHER_PATH", other, StringComparison.Ordinal);
        }
    }
}
