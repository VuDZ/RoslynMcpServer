using System.Runtime.InteropServices;
using RoslynMcpServer.Config;
using Xunit;

namespace RoslynMcpServer.Tests.Config;

public sealed class ServerUserDirectoriesTests
{
    [Fact]
    public void Windows_uses_local_app_data()
    {
        var result = Resolve(OSPlatform.Windows);

        Assert.Equal(Path.Combine(_local, "RoslynMcpServer"), result.ConfigurationDirectory);
        Assert.Equal(result.ConfigurationDirectory, result.StateDirectory);
        Assert.Equal(Path.Combine(result.StateDirectory, "logs"), result.LogDirectory);
        Assert.Equal(Path.Combine(result.StateDirectory, "usage"), result.UsageDirectory);
    }

    [Fact]
    public void Windows_prefers_the_local_app_data_environment_override()
    {
        var local = Path.Combine(_profile, "local-override");
        var result = Resolve(OSPlatform.Windows, new() { ["LOCALAPPDATA"] = local });

        Assert.Equal(Path.Combine(local, "RoslynMcpServer"), result.StateDirectory);
    }

    [Fact]
    public void Linux_separates_configuration_and_state_defaults()
    {
        var result = Resolve(OSPlatform.Linux);

        Assert.Equal(Path.Combine(_profile, ".config", "roslyn-mcp"), result.ConfigurationDirectory);
        Assert.Equal(Path.Combine(_profile, ".local", "state", "roslyn-mcp"), result.StateDirectory);
        Assert.Equal(Path.Combine(result.ConfigurationDirectory, "plugins"), result.PluginDirectory);
    }

    [Fact]
    public void Linux_honors_both_xdg_roots()
    {
        var config = Path.Combine(_profile, "config-override");
        var state = Path.Combine(_profile, "state-override");
        var result = Resolve(OSPlatform.Linux, new()
        {
            ["XDG_CONFIG_HOME"] = config,
            ["XDG_STATE_HOME"] = state,
        });

        Assert.Equal(Path.Combine(config, "roslyn-mcp"), result.ConfigurationDirectory);
        Assert.Equal(Path.Combine(state, "roslyn-mcp", "usage"), result.UsageDirectory);
    }

    [Fact]
    public void Mac_uses_application_support()
    {
        var result = Resolve(OSPlatform.OSX);

        Assert.Equal(Path.Combine(_profile, "Library", "Application Support", "RoslynMcpServer"), result.StateDirectory);
        Assert.Equal(result.StateDirectory, result.ConfigurationDirectory);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    [InlineData("mac")]
    public void Home_overrides_both_roots_and_log_directory_has_its_own_priority(string platformName)
    {
        var platform = platformName == "windows" ? OSPlatform.Windows
            : platformName == "mac" ? OSPlatform.OSX
            : OSPlatform.Linux;
        var home = Path.Combine(_profile, "custom-home");
        var logs = Path.Combine(_profile, "custom-logs");
        var result = Resolve(platform, new()
        {
            ["ROSLYN_MCP_HOME"] = home,
            ["ROSLYN_MCP_LOG_DIR"] = logs,
            ["XDG_CONFIG_HOME"] = Path.Combine(_profile, "ignored-config"),
            ["XDG_STATE_HOME"] = Path.Combine(_profile, "ignored-state"),
        });

        Assert.Equal(home, result.ConfigurationDirectory);
        Assert.Equal(home, result.StateDirectory);
        Assert.Equal(Path.Combine(home, "plugins"), result.PluginDirectory);
        Assert.Equal(Path.Combine(home, "usage"), result.UsageDirectory);
        Assert.Equal(logs, result.LogDirectory);
    }

    [Fact]
    public void Invalid_and_relative_environment_paths_fall_back_without_creating_directories()
    {
        Assert.False(Directory.Exists(_profile));
        var result = Resolve(OSPlatform.Linux, new()
        {
            ["ROSLYN_MCP_HOME"] = "\0",
            ["ROSLYN_MCP_LOG_DIR"] = "relative-logs",
            ["XDG_CONFIG_HOME"] = "relative-config",
            ["XDG_STATE_HOME"] = "\0",
        });

        Assert.Equal(Path.Combine(_profile, ".config", "roslyn-mcp"), result.ConfigurationDirectory);
        Assert.Equal(Path.Combine(_profile, ".local", "state", "roslyn-mcp", "logs"), result.LogDirectory);
        Assert.False(Directory.Exists(_profile));
    }

    private ServerUserDirectories Resolve(OSPlatform platform, Dictionary<string, string?>? environment = null) =>
        ServerUserDirectories.Resolve(
            platform,
            _profile,
            _local,
            name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);

    private readonly string _profile = Path.Combine(Path.GetTempPath(), "RoslynMcpUserDirectories-" + Guid.NewGuid().ToString("N"));
    private readonly string _local = Path.Combine(Path.GetTempPath(), "RoslynMcpLocalAppData");
}
