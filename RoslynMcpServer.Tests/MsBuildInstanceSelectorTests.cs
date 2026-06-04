using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class MsBuildInstanceSelectorTests
{
    [Fact]
    public void SelectBest_on_64bit_process_skips_Program_Files_x86_sdk()
    {
        var instances = new[]
        {
            new MsBuildInstanceCandidate(
                new Version(7, 0, 203),
                ".NET Core SDK 7.0.203",
                @"C:\Program Files (x86)\dotnet\sdk\7.0.203",
                @"C:\Program Files (x86)\dotnet\sdk\7.0.203"),
            new MsBuildInstanceCandidate(
                new Version(10, 0, 204),
                ".NET SDK 10.0.204",
                @"C:\Program Files\dotnet\sdk\10.0.204",
                @"C:\Program Files\dotnet\sdk\10.0.204"),
        };

        var selected = MsBuildInstanceSelector.SelectBest(instances, is64BitProcess: true);

        Assert.NotNull(selected);
        Assert.Equal(@"C:\Program Files\dotnet\sdk\10.0.204", selected.Value.MSBuildPath);
    }

    [Fact]
    public void SelectBest_prefers_Visual_Studio_2022_over_older_sdk()
    {
        var instances = new[]
        {
            new MsBuildInstanceCandidate(
                new Version(10, 0, 204),
                ".NET SDK 10.0.204",
                @"C:\Program Files\dotnet\sdk\10.0.204",
                @"C:\Program Files\dotnet\sdk\10.0.204"),
            new MsBuildInstanceCandidate(
                new Version(17, 14, 0),
                "Visual Studio Community 2022",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community"),
        };

        var selected = MsBuildInstanceSelector.SelectBest(instances, is64BitProcess: true);

        Assert.NotNull(selected);
        Assert.Contains("Visual Studio", selected.Value.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2022", selected.Value.MSBuildPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\dotnet\sdk\10.0.204", true)]
    [InlineData(@"C:\Program Files\dotnet\sdk\10.0.204", false)]
    [InlineData(@"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64", false)]
    public void Is32BitWindowsPath_detects_x86_program_files(string path, bool expected)
    {
        Assert.Equal(expected, MsBuildInstanceSelector.Is32BitWindowsPath(path));
    }

    [Fact]
    public void SelectBest_returns_null_when_only_x86_instances_and_64bit_process()
    {
        var instances = new[]
        {
            new MsBuildInstanceCandidate(
                new Version(10, 0, 204),
                ".NET SDK 10.0.204",
                @"C:\Program Files (x86)\dotnet\sdk\10.0.204",
                @"C:\Program Files (x86)\dotnet\sdk\10.0.204"),
        };

        Assert.Null(MsBuildInstanceSelector.SelectBest(instances, is64BitProcess: true));
    }
}
