using System.Runtime.InteropServices;

namespace RoslynMcpServer.Config;

/// <summary>Per-user configuration and state locations, resolved without creating directories.</summary>
public sealed class ServerUserDirectories
{
    public string ConfigurationDirectory { get; }
    public string StateDirectory { get; }
    public string LogDirectory { get; }
    public string UsageDirectory => Path.Combine(StateDirectory, "usage");
    public string PluginDirectory => Path.Combine(ConfigurationDirectory, "plugins");

    private ServerUserDirectories(string configurationDirectory, string stateDirectory, string logDirectory)
    {
        ConfigurationDirectory = configurationDirectory;
        StateDirectory = stateDirectory;
        LogDirectory = logDirectory;
    }

    public static ServerUserDirectories FromEnvironment()
    {
        var platform = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.Linux;
        return Resolve(
            platform,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable);
    }

    internal static ServerUserDirectories Resolve(
        OSPlatform platform,
        string userProfile,
        string localApplicationData,
        Func<string, string?> readEnvironment)
    {
        var profile = NormalizeAbsolute(userProfile) ?? AppContext.BaseDirectory;
        string configurationDirectory;
        string stateDirectory;
        var home = NormalizeAbsolute(readEnvironment("ROSLYN_MCP_HOME"));
        if (home is not null)
        {
            configurationDirectory = home;
            stateDirectory = home;
        }
        else if (platform == OSPlatform.Windows)
        {
            var local = NormalizeAbsolute(readEnvironment("LOCALAPPDATA"))
                ?? NormalizeAbsolute(localApplicationData)
                ?? Path.Combine(profile, "AppData", "Local");
            configurationDirectory = Path.Combine(local, "RoslynMcpServer");
            stateDirectory = configurationDirectory;
        }
        else if (platform == OSPlatform.OSX)
        {
            configurationDirectory = Path.Combine(profile, "Library", "Application Support", "RoslynMcpServer");
            stateDirectory = configurationDirectory;
        }
        else
        {
            var configHome = NormalizeAbsolute(readEnvironment("XDG_CONFIG_HOME")) ?? Path.Combine(profile, ".config");
            var stateHome = NormalizeAbsolute(readEnvironment("XDG_STATE_HOME")) ?? Path.Combine(profile, ".local", "state");
            configurationDirectory = Path.Combine(configHome, "roslyn-mcp");
            stateDirectory = Path.Combine(stateHome, "roslyn-mcp");
        }

        var logDirectory = NormalizeAbsolute(readEnvironment("ROSLYN_MCP_LOG_DIR"))
            ?? Path.Combine(stateDirectory, "logs");
        return new ServerUserDirectories(configurationDirectory, stateDirectory, logDirectory);
    }

    private static string? NormalizeAbsolute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            // Relative overrides must not make persistence depend on the harness working directory.
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
