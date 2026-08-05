using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceLoadGuidanceTests
{
    [Fact]
    public void FormatNoWorkspaceLoadedMessage_includes_candidates_under_cwd()
    {
        var root = CreateTempRoot();
        var originalCwd = Environment.CurrentDirectory;
        var originalEnv = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");

        try
        {
            Environment.SetEnvironmentVariable("ROSLYN_MCP_WORKSPACE", null);
            File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
            File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution />");
            Environment.CurrentDirectory = root;

            var message = WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("Error: No active workspace.");

            Assert.Contains("load_workspace", message, StringComparison.Ordinal);
            Assert.Contains("App.sln", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App.slnx", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".slnx", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not** accepted", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Candidate solution files:", message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            Environment.SetEnvironmentVariable("ROSLYN_MCP_WORKSPACE", originalEnv);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DiscoverSolutionCandidates_respects_maxCandidates()
    {
        var root = CreateTempRoot();
        var originalEnv = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");

        try
        {
            for (var i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(root, $"S{i}.sln"), string.Empty);
            }

            Environment.SetEnvironmentVariable("ROSLYN_MCP_WORKSPACE", root);
            var found = WorkspaceLoadGuidance.DiscoverSolutionCandidates(maxCandidates: 2, maxDepth: 2);
            Assert.Equal(2, found.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROSLYN_MCP_WORKSPACE", originalEnv);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWsGuide", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
