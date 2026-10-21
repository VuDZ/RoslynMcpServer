using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class DotNetCliRunnerLocaleTests
{
    [Fact]
    public void CreateProcessStartInfo_sets_english_cli_locale_and_disables_msbuild_node_reuse()
    {
        var psi = DotNetCliRunner.CreateProcessStartInfo("dotnet", "build", Path.GetTempPath());

        Assert.Equal("en-US", psi.Environment["DOTNET_CLI_UI_LANGUAGE"]);
        Assert.Equal("1", psi.Environment["MSBUILDDISABLENODEREUSE"]);
    }

    [Fact]
    public void BuildRunMetadata_surfaces_english_cli_locale()
    {
        var metadata = DotNetCliRunner.BuildRunMetadata(
            "dotnet",
            Path.GetTempPath(),
            combinedOutput: string.Empty,
            sdkVersion: null);

        Assert.Contains("DOTNET_CLI_UI_LANGUAGE", metadata, StringComparison.Ordinal);
        Assert.Contains("en-US", metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, @"Program Files\dotnet.")]
    [InlineData(false, "PATH or DOTNET_ROOT")]
    public void Failed_to_start_names_the_sdk_location_for_the_platform(bool windowsInstall, string expected)
    {
        var message = DotNetCliRunner.FailedToStartDotNetMessage("dotnet", windowsInstall);

        Assert.Contains(expected, message, StringComparison.Ordinal);
        if (windowsInstall)
        {
            Assert.DoesNotContain("DOTNET_ROOT", message, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Program Files", message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, @"Program Files\dotnet\sdk")]
    [InlineData(false, "DOTNET_ROOT or on PATH")]
    public void Missing_pin_warning_names_the_sdk_location_for_the_platform(bool windowsInstall, string expected)
    {
        var message = DotNetCliRunner.PinnedSdkNotFoundWarning("99.0.999", windowsInstall);

        Assert.Contains(expected, message, StringComparison.Ordinal);
        Assert.Contains("99.0.999", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRunMetadata_uses_the_current_platform_when_the_pin_is_missing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roslyn-mcp-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "global.json"),
                """{"sdk":{"version":"99.0.999"}}""");
            var metadata = DotNetCliRunner.BuildRunMetadata("dotnet", directory, string.Empty, sdkVersion: null);
            var expected = DotNetCliRunner.PinnedSdkNotFoundWarning("99.0.999");

            Assert.Contains(expected, metadata, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
