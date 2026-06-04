using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceRootResolverTests
{
    [Fact]
    public void ResolveDotNetWorkingDirectory_uses_global_json_ancestor()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "global.json"), """{ "sdk": { "version": "10.0.204" } }""");
        var sln = Path.Combine(nested, "App.sln");
        File.WriteAllText(sln, string.Empty);

        try
        {
            var workDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(sln);
            Assert.Equal(root, workDir);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryGetPinnedSdkVersion_reads_sdk_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "global.json"), """{ "sdk": { "version": "10.0.204" } }""");

        try
        {
            Assert.Equal("10.0.204", GlobalJsonSdkReader.TryGetPinnedSdkVersion(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
