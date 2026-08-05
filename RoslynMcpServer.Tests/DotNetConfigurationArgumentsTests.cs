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
    public void Append_adds_switch_when_set()
    {
        Assert.Equal(
            "build \"App.slnx\" -v:minimal -c \"Sit-Debug\"",
            DotNetConfigurationArguments.Append("build \"App.slnx\" -v:minimal", "Sit-Debug"));
        Assert.Equal(
            "build \"App.slnx\" -v:minimal",
            DotNetConfigurationArguments.Append("build \"App.slnx\" -v:minimal", null));
    }
}
