using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class DotNetHostResolverTests
{
    [Fact]
    [Trait("Category", TestCategories.RequiresWindows)]
    public void ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet()
    {
        Assert.True(Environment.Is64BitProcess, "The test project runs as x64.");

        lock (TestEnvironmentLocks.DotNetRoot)
        {
            var path = DotNetHostResolver.ResolveDotNetExecutable();
            Assert.False(MsBuildInstanceSelector.Is32BitWindowsPath(path));
            var configuredHost = DotNetHostResolver.ResolveConfiguredHost(
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));
            if (configuredHost is not null)
            {
                Assert.Equal(configuredHost, path);
            }
            else if (Path.IsPathRooted(path))
            {
                Assert.Contains("Program Files", path, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("(x86)", path, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ResolveConfiguredHost_skips_a_runtime_only_host_unless_dotnet_root_requests_it()
    {
        var temp = Directory.CreateTempSubdirectory("roslyn-mcp-host-");
        try
        {
            var runtimeHost = CreateHostMarker(temp.FullName, "runtime");
            var sdkHost = CreateHostMarker(temp.FullName, "sdk");
            var version = Directory.CreateDirectory(Path.Combine(temp.FullName, "sdk", "sdk", "10.0.100"));
            File.WriteAllText(Path.Combine(version.FullName, "dotnet.dll"), string.Empty);

            Assert.Equal(runtimeHost, DotNetHostResolver.ResolveConfiguredHost(Path.GetDirectoryName(runtimeHost), sdkHost));
            Assert.Equal(sdkHost, DotNetHostResolver.ResolveConfiguredHost(null, sdkHost));
            Assert.Null(DotNetHostResolver.ResolveConfiguredHost(null, runtimeHost));

            if (!OperatingSystem.IsWindows())
            {
                var bin = Directory.CreateDirectory(Path.Combine(temp.FullName, "bin"));
                var symlink = Path.Combine(bin.FullName, "dotnet");
                File.CreateSymbolicLink(symlink, sdkHost);

                Assert.Equal(sdkHost, DotNetHostResolver.ResolveConfiguredHost(null, symlink));
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private static string CreateHostMarker(string temp, string subdirectory)
    {
        var directory = Directory.CreateDirectory(Path.Combine(temp, subdirectory));
        var path = Path.Combine(directory.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
