using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class MsBuildWorkspacePropertiesTests
{
    [Fact]
    public void Create_omits_empty_properties()
    {
        var props = MsBuildWorkspaceProperties.Create(null, null);
        Assert.Empty(props);
    }

    [Fact]
    public void Create_sets_configuration_and_normalizes_platform()
    {
        var props = MsBuildWorkspaceProperties.Create("kart", "Any CPU");
        Assert.Equal("kart", props["Configuration"]);
        Assert.Equal("AnyCPU", props["Platform"]);
        Assert.False(props.ContainsKey("TargetFramework"));
    }

    [Fact]
    public void Create_sets_target_framework()
    {
        var props = MsBuildWorkspaceProperties.Create(null, null, " net10.0 ");
        Assert.Equal("net10.0", props["TargetFramework"]);
        Assert.Single(props);
    }

    [Fact]
    public void IsSameLoadCache_requires_path_and_properties()
    {
        Assert.True(
            MsBuildWorkspaceProperties.IsSameLoadCache(
                @"C:\src\App.sln",
                "kart",
                "x64",
                "net10.0",
                @"C:\src\App.sln",
                "kart",
                "x64",
                "net10.0",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(
            MsBuildWorkspaceProperties.IsSameLoadCache(
                @"C:\src\App.sln",
                "kart",
                "x64",
                "net10.0",
                @"C:\src\App.sln",
                "Debug",
                "x64",
                "net10.0",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(
            MsBuildWorkspaceProperties.IsSameLoadCache(
                @"C:\src\App.sln",
                "kart",
                "x64",
                "net10.0",
                @"C:\src\App.sln",
                "kart",
                "x64",
                "netstandard2.0",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(
            MsBuildWorkspaceProperties.IsSameLoadCache(
                @"C:\src\App.sln",
                null,
                null,
                null,
                @"C:\src\Other.sln",
                null,
                null,
                null,
                StringComparison.OrdinalIgnoreCase));
    }
}
