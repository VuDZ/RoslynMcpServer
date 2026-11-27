using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class ImmutableInputRootsTests : IDisposable
{
    public ImmutableInputRootsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "RoslynMcpImmutableRoots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Package_folders_are_read_after_large_top_level_values()
    {
        var packages = Path.Combine(_root, "custom-packages");
        var fallback = Path.Combine(_root, "fallback");
        var projectPath = WriteProject("App", $$"""
            {
              "version": 3,
              "targets": { "net10.0": { "Pkg/1.0.0": { "type": "package", "compile": { "lib/a.dll": {} } } } },
              "libraries": { "Pkg/1.0.0": { "files": [ "lib/a.dll" ] } },
              "packageFolders": { {{Json(packages + Path.DirectorySeparatorChar)}}: {}, {{Json(fallback)}}: {} },
              "project": { "restore": { "packagesPath": "ignored" } }
            }
            """);

        var roots = ImmutableInputRoots.Discover([projectPath], msBuildPath: null);

        Assert.Equal(2, roots.Count);
        Assert.Contains(roots, root => Same(root, packages));
        Assert.Contains(roots, root => Same(root, fallback));
    }

    [Theory]
    [InlineData("{ \"packageFolders\": [ \"/not-an-object\" ] }")]
    [InlineData("{ \"packageFolders\": { \"relative/packages\": {} } }")]
    [InlineData("{ \"packageFolders\": { ")]
    [InlineData("[]")]
    public void Unreadable_or_relative_package_folders_give_no_root(string assets)
    {
        var projectPath = WriteProject("Broken", assets);

        Assert.Empty(ImmutableInputRoots.Discover([projectPath], msBuildPath: null));
    }

    [Fact]
    public void Missing_assets_file_gives_no_root()
    {
        var projectPath = Path.Combine(_root, "NoRestore", "NoRestore.csproj");

        Assert.Empty(ImmutableInputRoots.Discover([projectPath, null], msBuildPath: null));
    }

    [Fact]
    public void Dotnet_sdk_msbuild_path_gives_sdk_packs_and_shared()
    {
        var dotnet = Path.Combine(_root, "dotnet");
        var msBuildPath = Path.Combine(dotnet, "sdk", "10.0.301") + Path.DirectorySeparatorChar;

        var roots = ImmutableInputRoots.DotNetRoots(msBuildPath);

        Assert.Equal(
            new[] { Path.Combine(dotnet, "sdk"), Path.Combine(dotnet, "packs"), Path.Combine(dotnet, "shared") },
            roots);
        Assert.Empty(ImmutableInputRoots.DotNetRoots(Path.Combine(_root, "VisualStudio", "MSBuild", "Current", "Bin")));
        Assert.Empty(ImmutableInputRoots.DotNetRoots(null));
    }

    [Fact]
    public void Containment_is_exact_and_does_not_match_a_user_directory_named_packs()
    {
        var dotnet = Path.Combine(_root, "dotnet");
        var roots = ImmutableInputRoots.Discover([], Path.Combine(dotnet, "sdk", "10.0.301"));

        Assert.True(ImmutableInputRoots.Contains(roots, Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref", "ref", "a.dll")));
        Assert.True(ImmutableInputRoots.Contains(roots, Path.Combine(dotnet, "sdk", "10.0.301", "Sdks", "config")));
        Assert.False(ImmutableInputRoots.Contains(roots, Path.Combine(_root, "repo", "packs", "Lib.dll")));
        Assert.False(ImmutableInputRoots.Contains(roots, Path.Combine(dotnet, "packs-extra", "Lib.dll")));
    }

    [Fact]
    public void Metadata_reference_under_an_immutable_root_gets_no_point_watcher()
    {
        var packages = Path.Combine(_root, "packages");
        var packageLib = Path.Combine(packages, "pkg", "1.0.0", "lib", "net10.0");
        var userPacks = Path.Combine(_root, "repo-packs", "packs");
        var packageDll = CopyReference(packageLib, "Pkg.dll");
        var userDll = CopyReference(userPacks, "Lib.dll");
        var projectPath = WriteProject("App", $$"""{ "packageFolders": { {{Json(packages)}}: {} } }""");
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "App",
            "App",
            LanguageNames.CSharp,
            filePath: projectPath));
        var solution = workspace.CurrentSolution
            .AddDocument(DocumentId.CreateNewId(projectId), "Program.cs", SourceText.From("class Program {}"), filePath: Path.Combine(_root, "App", "Program.cs"))
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(packageDll))
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(userDll));
        Assert.True(workspace.TryApplyChanges(solution));
        var roots = ImmutableInputRoots.Discover([projectPath], msBuildPath: null);

        var withRoots = WorkspaceInputMapBuilder.Build(workspace.CurrentSolution, Guid.NewGuid(), projectPath, loadGraphComplete: true, roots);
        var withoutRoots = WorkspaceInputMapBuilder.Build(workspace.CurrentSolution, Guid.NewGuid(), projectPath, loadGraphComplete: true);

        Assert.DoesNotContain(withRoots.Watchers, watcher => Same(watcher.Directory, packageLib));
        Assert.Contains(withRoots.Watchers, watcher => Same(watcher.Directory, userPacks) && !watcher.IncludeSubdirectories);
        Assert.NotNull(withRoots.Find(packageDll));
        Assert.Contains(withoutRoots.Watchers, watcher => Same(watcher.Directory, packageLib));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteProject(string name, string assets)
    {
        var projectDirectory = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
        var projectPath = Path.Combine(projectDirectory, name + ".csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(projectDirectory, "obj", "project.assets.json"), assets);
        return projectPath;
    }

    private static string CopyReference(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.Copy(typeof(Assert).Assembly.Location, path);
        return path;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static bool Same(string left, string right) =>
        InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private readonly string _root;
}
