using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class UtilityToolsSearchCodeTests
{
    [Fact]
    public async Task SearchCode_without_directory_uses_loaded_workspace_directory()
    {
        var workspaceRoot = CreateTempRoot();
        var externalRoot = CreateTempRoot();
        var originalCwd = Environment.CurrentDirectory;

        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "workspace.cs"), "// next version in workspace");

            Directory.CreateDirectory(Path.Combine(externalRoot, "Downloads"));
            File.WriteAllText(Path.Combine(externalRoot, "Downloads", "external.csv"), "next version outside workspace");

            Environment.CurrentDirectory = externalRoot;

            var manager = CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln"));
            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

            var result = await tool.SearchCode("next version");

            Assert.Contains($"in `{workspaceRoot}`", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"in `{externalRoot}`", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("workspace.cs", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("external.csv", result, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(externalRoot))
            {
                Directory.Delete(externalRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SearchCode_timeout_reports_partial_results()
    {
        var workspaceRoot = CreateTempRoot();
        var manager = CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln"));
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "a.cs"), "// next version line");

            var result = await tool.SearchCode("next version", maxResults: 1, maxScanSeconds: 1);
            Assert.Contains("Found 1 match(es)", result, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SearchCode_with_includeExtensions_can_search_non_cs_files()
    {
        var workspaceRoot = CreateTempRoot();
        var manager = CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln"));
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "a.csv"), "next version line");

            var defaultResult = await tool.SearchCode("next version");
            Assert.DoesNotContain("a.csv", defaultResult, StringComparison.OrdinalIgnoreCase);

            var csvResult = await tool.SearchCode("next version", includeExtensions: ".csv");
            Assert.Contains("a.csv", csvResult, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static SolutionManager CreateManagerWithLoadedPath(string loadedPath)
    {
        var manager = new SolutionManager(NullLogger<SolutionManager>.Instance);
        typeof(SolutionManager)
            .GetField("_loadedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, loadedPath);
        return manager;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpSearchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
