using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceDiskPathFilterTests
{
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
