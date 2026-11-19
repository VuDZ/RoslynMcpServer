using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceDiskPathFilterTests
{
    [Theory]
    [InlineData("Foo_abc_wpftmp.csproj", true)]
    [InlineData("Foo_ABC_WpFtMp.CsPrOj", true)]
    [InlineData("Foo_wpftmp.csproj", true)]
    [InlineData("Foo_wpftmp_Bar.csproj", false)]
    [InlineData("Foo_wpftmp.vbproj", false)]
    [InlineData("Foo_wpftmp.cs", false)]
    [InlineData("Foo_wpftmp/Bar.csproj", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Temporary_project_name_is_separate_from_graph_file_classification(string? path, bool expected)
    {
        Assert.Equal(expected, WorkspaceDiskPathFilter.IsWpfTemporaryProject(path));
        if (expected)
        {
            Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile(path));
        }
    }

    [Fact]
    public void IsIgnoredPath_skips_bin_obj_git_node_modules()
    {
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("src", "bin", "Debug", "A.cs")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("src", "obj", "Debug", "A.cs")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("repo", ".git", "HEAD")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("repo", "node_modules", "pkg", "index.cs")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("repo", "TestResults", "out.trx")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("repo", "artifacts", "A.cs")));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath(null));
        Assert.True(WorkspaceDiskPathFilter.IsIgnoredPath("  "));
    }

    [Fact]
    public void IsIgnoredPath_allows_source_tree()
    {
        Assert.False(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("src", "Services", "Foo.cs")));
        Assert.False(WorkspaceDiskPathFilter.IsIgnoredPath(Path.Combine("src", "binary", "Foo.cs")));
    }

    [Fact]
    public void IsCSharpSource_matches_cs_extension()
    {
        Assert.True(WorkspaceDiskPathFilter.IsCSharpSource("A.cs"));
        Assert.True(WorkspaceDiskPathFilter.IsCSharpSource("A.CS"));
        Assert.False(WorkspaceDiskPathFilter.IsCSharpSource("A.csproj"));
        Assert.False(WorkspaceDiskPathFilter.IsCSharpSource("A.txt"));
        Assert.False(WorkspaceDiskPathFilter.IsCSharpSource(null));
    }

    [Theory]
    [InlineData("artifacts/Repo/build", "artifacts/Repo/src/App/obj/index", true)]
    [InlineData("artifacts/Repo/build", "artifacts/Repo/src/App/A.cs", false)]
    [InlineData("bin/Repo", "bin/Repo/src/A.cs", false)]
    [InlineData("artifacts/Repo/build", "artifacts/Repo/build/obj/index", true)]
    [InlineData("artifacts/Repo", "artifacts/Repo", false)]
    [InlineData("Repo/build", "Repo/build-other/obj/index", true)]
    [InlineData("Repo/obj", "Repo/obj/src/A.cs", false)]
    public void Workspace_filter_checks_segments_after_the_common_prefix(string directory, string path, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpPathScope");
        Assert.Equal(
            expected,
            WorkspaceDiskPathFilter.IsIgnoredPath(
                Path.GetFullPath(Path.Combine(root, path)),
                Path.GetFullPath(Path.Combine(root, directory))));
    }

    [Theory]
    [Trait("Category", "RequiresWindows")]
    [InlineData(@"C:\artifacts\Repo\build", @"c:\ARTIFACTS\Repo\src\App\A.cs", false)]
    [InlineData(@"C:\artifacts\Repo\build", @"D:\artifacts\Repo\A.cs", true)]
    [InlineData(@"\\artifacts\first\build", @"\\artifacts\second\src\A.cs", false)]
    [InlineData(@"\\artifacts\share\build", @"\\artifacts\share\obj\index", true)]
    public void Workspace_filter_respects_windows_roots_without_filesystem_access(string directory, string path, bool expected)
    {
        Assert.Equal(expected, WorkspaceDiskPathFilter.IsIgnoredPath(path, directory));
    }

    [Fact]
    public void IsProjectGraphFile_matches_msbuild_and_global_json()
    {
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("App.csproj"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("App.sln"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("App.slnx"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("Directory.Build.props"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("Directory.Build.targets"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("Directory.Packages.props"));
        Assert.True(WorkspaceDiskPathFilter.IsProjectGraphFile("global.json"));
        Assert.False(WorkspaceDiskPathFilter.IsProjectGraphFile("Foo.cs"));
        Assert.False(WorkspaceDiskPathFilter.IsProjectGraphFile("nuget.config"));
    }

    [Fact]
    public void IsPathUnderDirectory_does_not_match_prefix_sibling()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpPathA"));
        var sibling = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpPathAB", "x.cs"));
        var child = Path.GetFullPath(Path.Combine(parent, "src", "x.cs"));

        Assert.False(WorkspaceDiskPathFilter.IsPathUnderDirectory(sibling, parent, StringComparison.OrdinalIgnoreCase));
        Assert.True(WorkspaceDiskPathFilter.IsPathUnderDirectory(child, parent, StringComparison.OrdinalIgnoreCase));
        Assert.True(WorkspaceDiskPathFilter.IsPathUnderDirectory(parent, parent, StringComparison.OrdinalIgnoreCase));
    }
}
