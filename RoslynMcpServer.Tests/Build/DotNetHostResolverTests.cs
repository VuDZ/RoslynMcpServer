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
        if (Path.IsPathRooted(path))
        {
            Assert.Contains("Program Files", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("(x86)", path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
