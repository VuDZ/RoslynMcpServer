using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class UtilityToolsSearchScopeTests
{
    [Fact]
    public void ResolveSearchRoots_omitted_directory_includes_external_project_and_solution_directory()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "repo"));
        var externalDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "external"));
        var sln = Path.Combine(slnDir, "App.sln");
        var nestedProject = Path.Combine(slnDir, "src", "App.csproj");
        var externalProject = Path.Combine(externalDir, "Lib.csproj");

        var roots = UtilityTools.ResolveSearchRoots(
            directoryPath: null,
            loadedFilePath: sln,
            projectFilePaths: [nestedProject, externalProject],
            processCurrentDirectory: Path.GetTempPath());

        Assert.Equal(2, roots.Count);
        Assert.Contains(slnDir, roots);
        Assert.Contains(externalDir, roots);
    }

    [Fact]
    public void ResolveSearchRoots_omitted_directory_keeps_loose_file_under_solution_directory_inside_a_root()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "repo"));
        var sln = Path.Combine(slnDir, "App.sln");
        var nestedProject = Path.Combine(slnDir, "src", "App.csproj");
        var looseCs = Path.Combine(slnDir, "Loose.cs");

        var roots = UtilityTools.ResolveSearchRoots(
            directoryPath: null,
            loadedFilePath: sln,
            projectFilePaths: [nestedProject],
            processCurrentDirectory: Path.GetTempPath());

        Assert.Contains(slnDir, roots);
        Assert.True(
            roots.Any(root => IsPathUnderRoot(looseCs, root)),
            $"Expected loose file `{looseCs}` under a search root. Roots: {string.Join(", ", roots)}");
    }

    [Fact]
    public void ResolveSearchRoots_explicit_directory_is_single_root_without_external_projects()
    {
        var slnDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "repo"));
        var externalDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "external"));
        var explicitDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpSearchScope", "explicit"));
        var sln = Path.Combine(slnDir, "App.sln");
        var externalProject = Path.Combine(externalDir, "Lib.csproj");

        var roots = UtilityTools.ResolveSearchRoots(
            directoryPath: explicitDir,
            loadedFilePath: sln,
            projectFilePaths: [externalProject],
            processCurrentDirectory: Path.GetTempPath());

        Assert.Equal([explicitDir], roots);
        Assert.DoesNotContain(externalDir, roots);
        Assert.DoesNotContain(slnDir, roots);
    }

    [Fact]
    public void ResolveSearchRoots_unix_style_absolute_inputs_stay_rooted()
    {
        var roots = UtilityTools.ResolveSearchRoots(
            directoryPath: null,
            loadedFilePath: "/home/foo/app.sln",
            projectFilePaths: ["/home/foo/src/App.csproj", "/opt/ext/Lib.csproj"],
            processCurrentDirectory: "/tmp");

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.True(Path.IsPathRooted(root), root));
        Assert.DoesNotContain(roots, root => root.Equals("home/foo", StringComparison.Ordinal)
            || root.StartsWith("home/", StringComparison.Ordinal));
    }

    private static bool IsPathUnderRoot(string filePath, string rootDirectory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullFile = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(rootDirectory);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar)
            && !fullRoot.EndsWith(Path.AltDirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return fullFile.StartsWith(fullRoot, comparison)
            || string.Equals(
                Path.GetDirectoryName(fullFile),
                Path.GetFullPath(rootDirectory),
                comparison);
    }
}
