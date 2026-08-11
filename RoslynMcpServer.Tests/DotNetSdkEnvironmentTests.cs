using System.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetSdkEnvironmentTests
{
    [Fact]
    public void ApplyPinnedSdk_without_global_json_strips_inherited_msbuild_sdk_overrides()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "roslyn-mcp-sdk-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        lock (TestEnvironmentLocks.DotNetRoot)
        {
            var previousSdks = Environment.GetEnvironmentVariable(DotNetSdkEnvironment.MsBuildSdksPathVariable);
            var previousExe = Environment.GetEnvironmentVariable(DotNetSdkEnvironment.MsBuildExePathVariable);
            var previousResolver = Environment.GetEnvironmentVariable(DotNetSdkEnvironment.SdkResolverSdksDirVariable);
            Environment.SetEnvironmentVariable(
                DotNetSdkEnvironment.MsBuildSdksPathVariable,
                @"C:\Program Files\dotnet\sdk\9.0.316\Sdks");
            Environment.SetEnvironmentVariable(
                DotNetSdkEnvironment.MsBuildExePathVariable,
                @"C:\Program Files\dotnet\sdk\9.0.316\MSBuild.dll");
            Environment.SetEnvironmentVariable(
                DotNetSdkEnvironment.SdkResolverSdksDirVariable,
                @"C:\Program Files\dotnet\sdk\9.0.316\Sdks");
            try
            {
                var psi = new ProcessStartInfo { FileName = "dotnet", Arguments = "--version" };
                DotNetSdkEnvironment.ApplyPinnedSdk(psi, workDir);

                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.MsBuildSdksPathVariable));
                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.MsBuildExePathVariable));
                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.SdkResolverSdksDirVariable));
                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.SdkResolverSdksVerVariable));
                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.SdkResolverCliDirVariable));
                Assert.False(psi.Environment.ContainsKey(DotNetSdkEnvironment.MsBuildExtensionsPathVariable));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DotNetSdkEnvironment.MsBuildSdksPathVariable, previousSdks);
                Environment.SetEnvironmentVariable(DotNetSdkEnvironment.MsBuildExePathVariable, previousExe);
                Environment.SetEnvironmentVariable(DotNetSdkEnvironment.SdkResolverSdksDirVariable, previousResolver);
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

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
