using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetConfigurationArgumentsTests
{
    [Fact]
    public void FormatSwitch_null_or_whitespace_returns_empty()
    {
        Assert.Equal(string.Empty, DotNetConfigurationArguments.FormatSwitch(null));
        Assert.Equal(string.Empty, DotNetConfigurationArguments.FormatSwitch("  "));
    }

    [Fact]
    public void FormatSwitch_wraps_name()
    {
        Assert.Equal(" -c \"Sit-Debug\"", DotNetConfigurationArguments.FormatSwitch("Sit-Debug"));
        Assert.Equal(" -c \"Dit-Debug\"", DotNetConfigurationArguments.FormatSwitch("  Dit-Debug  "));
    }

    [Fact]
    public void FormatSwitch_rejects_unsafe_characters()
    {
        Assert.Throws<ArgumentException>(() => DotNetConfigurationArguments.FormatSwitch("Sit\"Debug"));
        Assert.Throws<ArgumentException>(() => DotNetConfigurationArguments.FormatSwitch("a&b"));
    }

    [Fact]
    public void NormalizePlatform_maps_any_cpu_alias()
    {
        Assert.Equal("AnyCPU", DotNetConfigurationArguments.NormalizePlatform("Any CPU"));
        Assert.Equal("x64", DotNetConfigurationArguments.NormalizePlatform(" x64 "));
        Assert.Null(DotNetConfigurationArguments.NormalizePlatform(null));
    }

    [Fact]
    public void FormatPlatformProperty_wraps_name()
    {
        Assert.Equal(" -p:Platform=\"x64\"", DotNetConfigurationArguments.FormatPlatformProperty("x64"));
        Assert.Equal(" -p:Platform=\"AnyCPU\"", DotNetConfigurationArguments.FormatPlatformProperty("Any CPU"));
        Assert.Equal(string.Empty, DotNetConfigurationArguments.FormatPlatformProperty(null));
    }

    [Fact]
    public void Coalesce_prefers_explicit_over_cached()
    {
        Assert.Equal("Sit-Debug", DotNetConfigurationArguments.Coalesce("Sit-Debug", "Debug", "configuration"));
        Assert.Equal("Debug", DotNetConfigurationArguments.Coalesce(null, "Debug", "configuration"));
        Assert.Equal("x64", DotNetConfigurationArguments.CoalescePlatform(null, "x64"));
        Assert.Equal("AnyCPU", DotNetConfigurationArguments.CoalescePlatform("Any CPU", "x64"));
    }

    [Fact]
    public void Append_adds_switch_when_set()
    {
        Assert.Equal(
            "build \"App.slnx\" -v:minimal -c \"Sit-Debug\"",
            DotNetConfigurationArguments.Append("build \"App.slnx\" -v:minimal", "Sit-Debug"));
        Assert.Equal(
            "build \"App.slnx\" -v:minimal",
            DotNetConfigurationArguments.Append("build \"App.slnx\" -v:minimal", null));
    }

    [Fact]
    public void AppendPlatform_adds_property_when_set()
    {
        Assert.Equal(
            "build \"App.slnx\" -v:minimal -p:Platform=\"x64\"",
            DotNetConfigurationArguments.AppendPlatform("build \"App.slnx\" -v:minimal", "x64"));
    }
}
