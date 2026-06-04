using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetHostResolverTests
{
    [Fact]
    public void ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet()
    {
        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindows())
        {
            return;
        }

        var path = DotNetHostResolver.ResolveDotNetExecutable();
        Assert.False(MsBuildInstanceSelector.Is32BitWindowsPath(path));
        if (Path.IsPathRooted(path))
        {
            Assert.Contains("Program Files", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("(x86)", path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
