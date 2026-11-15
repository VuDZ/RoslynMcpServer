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

        var path = DotNetHostResolver.ResolveDotNetExecutable();
        Assert.False(MsBuildInstanceSelector.Is32BitWindowsPath(path));
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var configuredHost = string.IsNullOrWhiteSpace(dotnetRoot) ? null : Path.Combine(dotnetRoot.Trim(), "dotnet.exe");
        if (configuredHost is not null && File.Exists(configuredHost))
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
