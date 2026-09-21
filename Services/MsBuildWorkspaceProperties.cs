namespace RoslynMcpServer.Services;

/// <summary>Global properties passed to <c>MSBuildWorkspace.Create</c> (Configuration / Platform / TargetFramework).</summary>
public static class MsBuildWorkspaceProperties
{
    public static Dictionary<string, string> Create(
        string? configuration,
        string? platform,
        string? targetFramework = null)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var config = DotNetConfigurationArguments.Normalize(configuration, nameof(configuration));
        var plat = DotNetConfigurationArguments.NormalizePlatform(platform);
        var tfm = DotNetConfigurationArguments.Normalize(targetFramework, nameof(targetFramework));
        if (config is not null)
        {
            props["Configuration"] = config;
        }

        if (plat is not null)
        {
            props["Platform"] = plat;
        }

        if (tfm is not null)
        {
            props["TargetFramework"] = tfm;
        }

        return props;
    }

    public static bool IsSameLoadCache(
        string? loadedPath,
        string? loadedConfiguration,
        string? loadedPlatform,
        string? loadedTargetFramework,
        string fullPath,
        string? configuration,
        string? platform,
        string? targetFramework,
        StringComparison pathComparison)
    {
        if (string.IsNullOrWhiteSpace(loadedPath)
            || !string.Equals(loadedPath, fullPath, pathComparison))
        {
            return false;
        }

        return string.Equals(loadedConfiguration, configuration, StringComparison.OrdinalIgnoreCase)
               && string.Equals(loadedPlatform, platform, StringComparison.OrdinalIgnoreCase)
               && string.Equals(loadedTargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cache key for <c>load_workspace</c> / config lazy load: compare only arguments that were
    /// actually passed. Omitted (<see langword="null"/> or whitespace → normalized null) values are
    /// not compared and do not clear already-loaded MSBuild properties.
    /// </summary>
    /// <remarks>
    /// Do not use <see cref="IsSameLoadCache"/> for this path — it treats an omitted argument as
    /// unequal to a loaded value and would reopen without that property.
    /// </remarks>
    public static bool MatchesPassedLoadArguments(
        string? loadedPath,
        string? loadedConfiguration,
        string? loadedPlatform,
        string? loadedTargetFramework,
        string fullPath,
        string? passedConfiguration,
        string? passedPlatform,
        string? passedTargetFramework,
        StringComparison pathComparison)
    {
        if (string.IsNullOrWhiteSpace(loadedPath)
            || !string.Equals(loadedPath, fullPath, pathComparison))
        {
            return false;
        }

        if (passedConfiguration is not null
            && !string.Equals(loadedConfiguration, passedConfiguration, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (passedPlatform is not null
            && !string.Equals(loadedPlatform, passedPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (passedTargetFramework is not null
            && !string.Equals(loadedTargetFramework, passedTargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
