using System.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetSdkEnvironmentTests
{
    [Fact]
    public void ApplyPinnedSdk_sets_official_resolver_variables_when_sdk_resolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-sdk-" + Guid.NewGuid().ToString("N"));
        var sdkDir = Path.Combine(root, "sdk", "10.0.999");
        var sdksDir = Path.Combine(sdkDir, "Sdks");
        Directory.CreateDirectory(sdksDir);
        File.WriteAllText(Path.Combine(sdkDir, "MSBuild.dll"), string.Empty);
        File.WriteAllText(Path.Combine(sdkDir, "Microsoft.Build.dll"), string.Empty);
        File.WriteAllText(
            Path.Combine(root, "global.json"),
            """
            { "sdk": { "version": "10.0.999" } }
            """);

        lock (TestEnvironmentLocks.DotNetRoot)
        {
            var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);
            try
            {
                var psi = new ProcessStartInfo { FileName = "dotnet", Arguments = "--version" };
                DotNetSdkEnvironment.ApplyPinnedSdk(psi, root);

                Assert.True(psi.Environment.ContainsKey("PATH"));
                Assert.Equal(Path.Combine(sdkDir, "MSBuild.dll"), psi.Environment[DotNetSdkEnvironment.MsBuildExePathVariable]);
                Assert.Equal(sdksDir, psi.Environment[DotNetSdkEnvironment.SdkResolverSdksDirVariable]);
                Assert.Equal("10.0.999", psi.Environment[DotNetSdkEnvironment.SdkResolverSdksVerVariable]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
