using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SolutionManagerPathResolutionTests
{
    [Fact]
    public void ResolvePathAgainstWorkspace_keeps_absolute_path()
    {
        var manager = CreateManager();
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpTests", "a.cs"));

        var resolved = manager.ResolvePathAgainstWorkspace(absolute);

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void ResolvePathAgainstWorkspace_resolves_relative_path_from_loaded_workspace()
    {
        var root = CreateTempRoot();
        var solutionPath = Path.Combine(root, "BrqMover.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var manager = CreateManager(solutionPath);

        try
        {
            var resolved = manager.ResolvePathAgainstWorkspace("src/BrqMover.Application/Services/Foo.cs");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "src", "BrqMover.Application", "Services", "Foo.cs")),
                resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePathAgainstWorkspace_falls_back_to_current_directory_when_workspace_missing()
    {
        var manager = CreateManager();
        var original = Environment.CurrentDirectory;
        var temp = CreateTempRoot();
        Directory.CreateDirectory(temp);

        try
        {
            Environment.CurrentDirectory = temp;
            var resolved = manager.ResolvePathAgainstWorkspace("src/App.cs");
            Assert.Equal(Path.GetFullPath(Path.Combine(temp, "src", "App.cs")), resolved);
        }
        finally
        {
            Environment.CurrentDirectory = original;
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetMethodBody_resolves_relative_path_against_loaded_workspace()
    {
        var root = CreateTempRoot();
        var solutionPath = Path.Combine(root, "BrqMover.sln");
        var sourcePath = Path.Combine(root, "src", "BrqMover.Application", "Services", "TopLevelMoveFlagsAnnotator.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            sourcePath,
            """
            namespace BrqMover.Application.Services;

            public sealed class TopLevelMoveFlagsAnnotator
            {
                public void Apply()
                {
                    // body
                }
            }
            """);

        var manager = CreateManager(solutionPath);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        try
        {
            var result = await tool.GetMethodBody(
                "src/BrqMover.Application/Services/TopLevelMoveFlagsAnnotator.cs",
                "TopLevelMoveFlagsAnnotator",
                "Apply");

            Assert.DoesNotContain("File not found", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("void Apply()", result, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SolutionManager CreateManager(string? loadedPath = null)
    {
        var manager = new SolutionManager(NullLogger<SolutionManager>.Instance);
        if (!string.IsNullOrWhiteSpace(loadedPath))
        {
            typeof(SolutionManager)
                .GetField("_loadedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, loadedPath);
        }

        return manager;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
