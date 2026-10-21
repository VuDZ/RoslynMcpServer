using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

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

    [Fact]
    public void NonWindows_sdk_roots_include_ubuntu_packages_and_the_user_install()
    {
        var roots = MsBuildInstanceSelector.NonWindowsSdkRoots("/opt/dotnet", "/home/builder");

        Assert.Equal(
            [
                Path.Combine("/opt/dotnet", "sdk"),
                "/usr/local/share/dotnet/sdk",
                "/usr/share/dotnet/sdk",
                "/usr/lib/dotnet/sdk",
                Path.Combine("/home/builder", ".dotnet", "sdk"),
            ],
            roots);
    }

    [Fact]
    public void NonWindows_sdk_roots_skip_blank_environment_and_profile()
    {
        var roots = MsBuildInstanceSelector.NonWindowsSdkRoots("  ", null);

        Assert.Equal(
            ["/usr/local/share/dotnet/sdk", "/usr/share/dotnet/sdk", "/usr/lib/dotnet/sdk"],
            roots);
    }

    [Fact]
    public void FindLatestSdkDirectory_skips_roots_without_msbuild_and_picks_the_highest_folder_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-sdk-" + Guid.NewGuid().ToString("N"));
        var empty = Path.Combine(root, "empty", "sdk");
        var hit = Path.Combine(root, "hit", "sdk");
        var older = Path.Combine(hit, "10.0.100");
        var newer = Path.Combine(hit, "10.0.300");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        File.WriteAllText(Path.Combine(older, "Microsoft.Build.dll"), string.Empty);
        File.WriteAllText(Path.Combine(newer, "Microsoft.Build.dll"), string.Empty);
        try
        {
            var found = MsBuildInstanceSelector.FindLatestSdkDirectory(
                [Path.Combine(root, "missing", "sdk"), empty, hit]);

            Assert.Equal(newer, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
