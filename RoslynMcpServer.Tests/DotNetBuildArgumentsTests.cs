using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetBuildArgumentsTests
{
    [Fact]
    public void Normalize_null_or_whitespace_returns_null()
    {
        Assert.Null(DotNetBuildArguments.Normalize(null));
        Assert.Null(DotNetBuildArguments.Normalize("  "));
        Assert.Null(DotNetBuildArguments.Normalize(string.Empty));
    }

    [Fact]
    public void Normalize_trims_valid_args()
    {
        Assert.Equal(
            "-p:TreatWarningsAsErrors=false -p:SkipInvalidConfigurations=true",
            DotNetBuildArguments.Normalize("  -p:TreatWarningsAsErrors=false -p:SkipInvalidConfigurations=true  "));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("'")]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("<")]
    [InlineData(">")]
    public void Normalize_rejects_unsafe_characters(string bad)
    {
        Assert.Throws<ArgumentException>(() => DotNetBuildArguments.Normalize($"-p:Foo=1{bad}"));
    }

    [Fact]
    public void FormatSuffix_and_Append_add_leading_space()
    {
        Assert.Equal(string.Empty, DotNetBuildArguments.FormatSuffix(null));
        Assert.Equal(
            " -p:TreatWarningsAsErrors=false",
            DotNetBuildArguments.FormatSuffix("-p:TreatWarningsAsErrors=false"));
        Assert.Equal(
            "build \"App.sln\" -v:minimal -p:TreatWarningsAsErrors=false",
            DotNetBuildArguments.Append("build \"App.sln\" -v:minimal", "-p:TreatWarningsAsErrors=false"));
        Assert.Equal("build \"App.sln\"", DotNetBuildArguments.Append("build \"App.sln\"", null));
    }

    [Fact]
    public void FormatMetadata_none_or_backticks()
    {
        Assert.Equal("(none)", DotNetBuildArguments.FormatMetadata(null));
        Assert.Equal("(none)", DotNetBuildArguments.FormatMetadata("  "));
        Assert.Equal("`-p:Foo=1`", DotNetBuildArguments.FormatMetadata("-p:Foo=1"));
    }

    [Fact]
    public async Task ApplySessionBuildArgs_replaces_without_workspace_reload()
    {
        var manager = new SolutionManager(NullLogger<SolutionManager>.Instance);
        manager.ApplySessionBuildArgs("-p:Foo=1");
        Assert.Equal("-p:Foo=1", manager.LoadedBuildArgs);

        manager.ApplySessionBuildArgs("-p:Bar=2");
        Assert.Equal("-p:Bar=2", manager.LoadedBuildArgs);

        manager.ApplySessionBuildArgs(null);
        Assert.Null(manager.LoadedBuildArgs);

        manager.ApplySessionBuildArgs("-p:Foo=1");
        await manager.ClearWorkspaceAsync();
        Assert.Null(manager.LoadedBuildArgs);
    }
}
