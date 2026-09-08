using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class NuGetFallbackAssemblyResolverTests
{
    [Fact]
    public void TryFindAssemblyDll_finds_newtonsoft_json_when_package_present()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
        if (!Directory.Exists(nugetRoot))
        {
            return;
        }

        var path = NuGetFallbackAssemblyResolver.TryFindAssemblyDll("Newtonsoft.Json", nugetRoot);
        if (File.Exists(path ?? string.Empty))
        {
            Assert.EndsWith("Newtonsoft.Json.dll", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TryFindAssemblyDll_finds_system_io_ports_via_bcl_map_when_package_present()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
        var mappedDir = Path.Combine(nugetRoot, "system.io.ports");
        if (!Directory.Exists(mappedDir))
        {
            return;
        }

        var path = NuGetFallbackAssemblyResolver.TryFindAssemblyDll("System.IO.Ports", nugetRoot);
        Assert.False(string.IsNullOrEmpty(path));
        Assert.EndsWith("System.IO.Ports.dll", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}unix{Path.DirectorySeparatorChar}", path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFindAssemblyDll_prefers_bcl_mapped_lib_over_unix_runtime()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpNuGetFallback", Guid.NewGuid().ToString("N"));
        try
        {
            var winDll = Path.Combine(root, "system.io.ports", "9.0.0", "lib", "net8.0", "System.IO.Ports.dll");
            var unixDll = Path.Combine(
                root,
                "system.io.ports",
                "9.0.0",
                "runtimes",
                "unix",
                "lib",
                "net8.0",
                "System.IO.Ports.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(winDll)!);
            Directory.CreateDirectory(Path.GetDirectoryName(unixDll)!);
            File.WriteAllBytes(winDll, [0]);
            File.WriteAllBytes(unixDll, [0]);

            Assert.True(BclAssemblyPackageMap.TryGetPackageId("System.IO.Ports", out var packageId));
            Assert.Equal("system.io.ports", packageId);

            var path = NuGetFallbackAssemblyResolver.TryFindAssemblyDll("System.IO.Ports", root);
            Assert.Equal(winDll, path);
            Assert.DoesNotContain(
                $"{Path.DirectorySeparatorChar}unix{Path.DirectorySeparatorChar}",
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
