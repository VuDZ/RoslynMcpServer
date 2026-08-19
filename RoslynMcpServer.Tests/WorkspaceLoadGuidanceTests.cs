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
    public void FormatClientCancelledWorkspaceLoadMessage_is_explicit_abort_not_msbuild()
    {
        var message = WorkspaceLoadGuidance.FormatClientCancelledWorkspaceLoadMessage(
            @"C:\repo\Tests.sln");

        Assert.Contains("Workspace Load Cancelled (client abort)", message, StringComparison.Ordinal);
        Assert.Contains("not an MSBuild", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timeout", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tests.sln", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workspace Load Failed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatMissingTargetFrameworkWorkspaceLoadMessage_is_not_nu1701_and_hints_bazel()
    {
        var diagnostics = new[]
        {
            "Failure: Msbuild failed when processing the file 'D:\\m\\src\\product\\kavkis\\Autotests\\_sln_kart\\generated\\Foo.csproj' with message: The \"ResolvePackageAssets\" task was not given a value for the required parameter \"TargetFramework\".",
        };

        var message = WorkspaceLoadGuidance.FormatMissingTargetFrameworkWorkspaceLoadMessage(
            @"D:\m\src\product\kavkis\Autotests\_sln_kart\ide_kart_m_src.sln",
            diagnostics,
            configuration: null,
            platform: null);

        Assert.Contains("empty TargetFramework", message, StringComparison.Ordinal);
        Assert.Contains("not NU1701", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configuration", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bazel/generated", message, StringComparison.Ordinal);
        Assert.Contains("Foo.csproj", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Successfully loaded", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEmptyTestListMessage_flags_csproj_scope()
    {
        var message = WorkspaceLoadGuidance.FormatEmptyTestListMessage(
            @"C:\repo\Common\Common.csproj",
            projectCount: 1);

        Assert.Contains("No tests found", message, StringComparison.Ordinal);
        Assert.Contains("Agent signal", message, StringComparison.Ordinal);
        Assert.Contains("Common.csproj", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single `.csproj`", message, StringComparison.Ordinal);
        Assert.Contains("Projects in workspace:** 1", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatNoMatchingTestsAgentHint_detects_path_mismatch_and_suffix_mode()
    {
        var message = WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(
            loadedRoslynWorkspacePath: @"C:\repo\Common\Common.csproj",
            filterDescription: "Name suffix `.FooTests.Bar`",
            testTargetPath: @"C:\repo\Tests.sln");

        Assert.Contains("Agent diagnostics", message, StringComparison.Ordinal);
        Assert.Contains("Mismatch", message, StringComparison.Ordinal);
        Assert.Contains("name-suffix fallback", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tests.sln", message, StringComparison.OrdinalIgnoreCase);
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
