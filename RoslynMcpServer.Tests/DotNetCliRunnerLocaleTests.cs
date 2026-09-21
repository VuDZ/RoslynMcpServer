using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

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
}
