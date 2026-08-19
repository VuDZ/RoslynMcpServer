namespace RoslynMcpServer.Services;

/// <summary>Global properties passed to <c>MSBuildWorkspace.Create</c> (Configuration / Platform only).</summary>
public static class MsBuildWorkspaceProperties
{
    public static Dictionary<string, string> Create(string? configuration, string? platform)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var config = DotNetConfigurationArguments.Normalize(configuration, nameof(configuration));
        var plat = DotNetConfigurationArguments.NormalizePlatform(platform);
        if (config is not null)
        {
            props["Configuration"] = config;
        }

        if (plat is not null)
        {
            props["Platform"] = plat;
        }

        return props;
    }

    public static bool IsSameLoadCache(
        string? loadedPath,
        string? loadedConfiguration,
        string? loadedPlatform,
        string fullPath,
        string? configuration,
        string? platform,
        StringComparison pathComparison)
    {
        if (string.IsNullOrWhiteSpace(loadedPath)
            || !string.Equals(loadedPath, fullPath, pathComparison))
        {
            return false;
        }

        return string.Equals(loadedConfiguration, configuration, StringComparison.OrdinalIgnoreCase)
               && string.Equals(loadedPlatform, platform, StringComparison.OrdinalIgnoreCase);
    }
}
