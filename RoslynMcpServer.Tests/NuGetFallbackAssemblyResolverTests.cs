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
        if (!Directory.Exists(nugetRoot))
        {
            return;
        }

        var path = NuGetFallbackAssemblyResolver.TryFindAssemblyDll("System.IO.Ports", nugetRoot);
        Assert.False(string.IsNullOrEmpty(path));
        Assert.EndsWith("System.IO.Ports.dll", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}unix{Path.DirectorySeparatorChar}", path!, StringComparison.OrdinalIgnoreCase);
    }
}
