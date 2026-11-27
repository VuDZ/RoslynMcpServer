using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class DotNetSdkResolverTests
{
    [Theory]
    [InlineData("10.0.402", true, true)]
    [InlineData("10.0.301", true, false)]
    [InlineData("10.0.402", false, false)]
    public void FromHostResult_uses_only_the_versioned_sdk_path_confirmed_by_the_host(
        string version,
        bool createMsBuild,
        bool expectSdk)
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn mcp custom SDK " + Guid.NewGuid().ToString("N"));
        var sdkPath = Path.Combine(root, "custom-root", "sdk", "10.0.402");
        Directory.CreateDirectory(sdkPath);
        try
        {
            if (createMsBuild)
            {
                File.WriteAllText(Path.Combine(sdkPath, "Microsoft.Build.dll"), string.Empty);
                File.WriteAllText(Path.Combine(sdkPath, "MSBuild.dll"), string.Empty);
            }

            var globalJson = Path.Combine(root, "global.json");
            var sdk = DotNetSdkResolver.FromHostResult(
                globalJson,
                "10.0.301",
                version,
                $"Found global.json [{globalJson}]\nSDK path resolved to [{sdkPath}]\n");

            Assert.Equal(globalJson, sdk.GlobalJsonPath);
            Assert.Equal("10.0.301", sdk.PinnedVersion);
            Assert.Equal(version, sdk.ResolvedVersion);
            Assert.Equal(expectSdk ? sdkPath : null, sdk.SdkDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_discovery_returns_only_installed_sdk_candidates()
    {
        var root = Directory.CreateTempSubdirectory("roslyn-mcp-startup-sdk-");
        try
        {
            var sdkDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "local", "sdk", "10.0.987"));
            File.WriteAllText(Path.Combine(root.FullName, "global.json"), """
                { "sdk": { "version": "10.0.987", "rollForward": "disable", "paths": ["local"] } }
                """);
            // These are filesystem metadata and empty markers. No executable SDK is needed.
            File.WriteAllText(Path.Combine(sdkDirectory.FullName, ".version"), "10.0.987");
            File.WriteAllText(Path.Combine(sdkDirectory.FullName, "dotnet.dll"), string.Empty);
            File.WriteAllText(Path.Combine(sdkDirectory.FullName, "Microsoft.Build.dll"), string.Empty);

            var candidates = MsBuildBootstrapper.QueryStartupInstances(root.FullName);

            Assert.NotEmpty(candidates);
            Assert.DoesNotContain(candidates, i => DotNetSdkEnvironment.PathsEqual(i.MSBuildPath, sdkDirectory.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_uses_the_host_policy_in_the_command_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-sdk-policy-" + Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var globalJson = Path.Combine(root, "global.json");
            File.WriteAllText(globalJson, """
                {
                  "sdk": { "version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": false }
                }
                """);
            var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
            var actual = await DotNetSdkResolver.ResolveAsync(
                dotnet, workingDirectory, DotNetSdkResolver.DefaultTimeout, CancellationToken.None);

            Assert.Equal(globalJson, actual.GlobalJsonPath);
            Assert.Equal("10.0.100", actual.PinnedVersion);
            Assert.StartsWith("10.0.", actual.ResolvedVersion, StringComparison.Ordinal);
            Assert.Equal(actual.ResolvedVersion, Path.GetFileName(actual.SdkDirectory));
            Assert.True(File.Exists(actual.MsBuildDllPath));
            var startup = MsBuildBootstrapper.QueryStartupInstances(workingDirectory);
            Assert.NotEmpty(startup);
            Assert.True(DotNetSdkEnvironment.PathsEqual(actual.SdkDirectory!, startup[0].MSBuildPath));

            File.WriteAllText(globalJson, """
                { "sdk": { "version": "99.0.999", "rollForward": "disable" } }
                """);
            var unavailable = await DotNetSdkResolver.ResolveAsync(
                dotnet, workingDirectory, DotNetSdkResolver.DefaultTimeout, CancellationToken.None);
            Assert.Equal("99.0.999", unavailable.PinnedVersion);
            Assert.Null(unavailable.ResolvedVersion);
            Assert.Null(unavailable.SdkDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
