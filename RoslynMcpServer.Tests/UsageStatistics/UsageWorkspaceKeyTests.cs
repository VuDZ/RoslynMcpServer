using RoslynMcpServer.Diagnostics.UsageStatistics;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageWorkspaceKeyTests
{
    [Fact]
    public void Equivalent_normalized_properties_share_a_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "usage-key", "App.sln");
        Assert.Equal(
            UsageWorkspaceKey.Create(path, " Release ", "Any CPU", "net10.0"),
            UsageWorkspaceKey.Create(Path.Combine(Path.GetDirectoryName(path)!, ".", "App.sln"), "release", "AnyCPU", "NET10.0"));
    }

    [Fact]
    public void Missing_properties_and_different_frameworks_remain_distinct()
    {
        var path = Path.Combine(Path.GetTempPath(), "usage-key", "App.csproj");
        Assert.NotEqual(
            UsageWorkspaceKey.Create(path, null, null, null),
            UsageWorkspaceKey.Create(path, "Debug", null, null));
        Assert.NotEqual(
            UsageWorkspaceKey.Create(path, null, null, "net10.0"),
            UsageWorkspaceKey.Create(path, null, null, "net8.0"));
        Assert.NotEqual(
            UsageWorkspaceKey.Create(path, "a:b", null, "c"),
            UsageWorkspaceKey.Create(path, "a", null, "b:c"));
    }

    [Fact]
    public void Path_case_follows_the_operating_system()
    {
        var first = UsageWorkspaceKey.Create(Path.Combine(Path.GetTempPath(), "UsageKey", "App.sln"), null, null, null);
        var second = UsageWorkspaceKey.Create(Path.Combine(Path.GetTempPath(), "UsageKey", "app.sln"), null, null, null);
        Assert.Equal(OperatingSystem.IsWindows(), first == second);
    }

    [Theory]
    [InlineData("RssAnon: 12 kB\nVmSwap: 7 kB\n", 19456L)]
    [InlineData("RssAnon:\t0 kB\nVmSwap:\t0 kB\n", 0L)]
    [InlineData("VmRSS: 50 kB\nVmSwap: 1 kB\n", null)]
    [InlineData("RssAnon: 20 kB\n", null)]
    public void Linux_primary_memory_is_anonymous_plus_swap_or_unknown(string status, long? expected)
    {
        Assert.Equal(expected, UsageMemorySampler.ParseLinuxPrivateMemory(status));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    public void Collection_is_opt_out(string? value, bool expected) =>
        Assert.Equal(expected, UsageStatisticsService.IsEnabled(value));
}
