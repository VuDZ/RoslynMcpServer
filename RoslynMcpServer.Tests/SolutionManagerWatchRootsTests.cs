using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SolutionManagerWatchRootsTests
{
    [Fact]
    public void ComputeWatchRoots_drops_nested_project_directory_under_solution_directory()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo"));
        var sln = Path.Combine(slnDir, "App.sln");
        var nestedProject = Path.Combine(slnDir, "src", "Lib.csproj");

        var roots = SolutionManager.ComputeWatchRoots(sln, [nestedProject]);

        Assert.Equal([slnDir], roots);
    }

    [Fact]
    public void ComputeWatchRoots_keeps_external_project_directory_as_its_own_root()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo"));
        var externalDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "external"));
        var sln = Path.Combine(slnDir, "App.sln");
        var nestedProject = Path.Combine(slnDir, "src", "App.csproj");
        var externalProject = Path.Combine(externalDir, "Lib.csproj");

        var roots = SolutionManager.ComputeWatchRoots(sln, [nestedProject, externalProject]);

        Assert.Equal(2, roots.Count);
        Assert.Contains(slnDir, roots);
        Assert.Contains(externalDir, roots);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(slnDir, "src")), roots);
    }

    [Fact]
    public void ComputeWatchRoots_ignores_empty_whitespace_and_invalid_paths()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo"));
        var sln = Path.Combine(slnDir, "App.sln");
        var invalid = "bad" + Path.GetInvalidPathChars()[0] + ".csproj";

        var roots = SolutionManager.ComputeWatchRoots(
            sln,
            [null, string.Empty, "   ", invalid]);

        Assert.Equal([slnDir], roots);
    }

    [Fact]
    public void ComputeWatchRoots_does_not_treat_prefix_sibling_as_nested()
    {
        var repoDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo"));
        var siblingDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo-other"));
        var sln = Path.Combine(repoDir, "App.sln");
        var siblingProject = Path.Combine(siblingDir, "Lib.csproj");

        var roots = SolutionManager.ComputeWatchRoots(sln, [siblingProject]);

        Assert.Equal(2, roots.Count);
        Assert.Contains(repoDir, roots);
        Assert.Contains(siblingDir, roots);
    }

    [Fact]
    public void ComputeWatchRoots_unix_style_absolute_paths_stay_rooted()
    {
        var roots = SolutionManager.ComputeWatchRoots(
            "/home/foo/app.sln",
            ["/home/foo/src/App.csproj", "/opt/ext/Lib.csproj"]);

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.True(Path.IsPathRooted(root), root));
        Assert.DoesNotContain(roots, root => root.Equals("home/foo", StringComparison.Ordinal)
            || root.StartsWith("home/", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeWatchRoots_null_project_list_keeps_loaded_directory()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatch", "repo"));
        var sln = Path.Combine(slnDir, "App.sln");

        var roots = SolutionManager.ComputeWatchRoots(sln, projectFilePaths: null);

        Assert.Equal([slnDir], roots);
    }
}
