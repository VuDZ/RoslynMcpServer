namespace RoslynMcpServer.Services;

/// <summary>
/// Formats optional MSBuild <c>-c</c> / <c>-p:Platform</c> for <c>dotnet build|test</c>
/// and sanitizes names used as MSBuildWorkspace global properties.
/// </summary>
public static class DotNetConfigurationArguments
{
    /// <summary>
    /// Trims and validates a configuration or platform name. Returns <see langword="null"/> when omitted.
    /// </summary>
    /// <exception cref="ArgumentException">When the name is empty after trim or contains unsafe characters.</exception>
    public static string? Normalize(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = value.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException($"{paramName} is empty.", paramName);
        }

        // Keep CLI quoting safe: solution config names are like Sit-Debug / Dit-Debug / kart.
        foreach (var ch in name)
        {
            if (ch is '"' or '\'' or '&' or '|' or ';' or '<' or '>' or '\n' or '\r' or '\0')
            {
                throw new ArgumentException(
                    $"{paramName} contains an invalid character: '{ch}'.",
                    paramName);
            }
        }

        return name;
    }

    /// <summary>
    /// Same as <see cref="Normalize"/> plus the well-known sln alias <c>Any CPU</c> → <c>AnyCPU</c>.
    /// </summary>
    public static string? NormalizePlatform(string? platform)
    {
        var name = Normalize(platform, nameof(platform));
        if (name is null)
        {
            return null;
        }

        return name.Equals("Any CPU", StringComparison.OrdinalIgnoreCase) ? "AnyCPU" : name;
    }

    public static string? Coalesce(string? explicitValue, string? cached, string paramName) =>
        Normalize(explicitValue, paramName) ?? cached;

    public static string? CoalescePlatform(string? explicitValue, string? cached) =>
        NormalizePlatform(explicitValue) ?? cached;

    /// <summary>
    /// Returns a leading-space fragment <c> -c "Name"</c>, or empty when <paramref name="configuration"/> is omitted.
    /// </summary>
    public static string FormatSwitch(string? configuration)
    {
        var name = Normalize(configuration, nameof(configuration));
        return name is null ? string.Empty : $" -c \"{name}\"";
    }

    /// <summary>
    /// Returns a leading-space fragment <c> -p:Platform="Name"</c>, or empty when omitted.
    /// </summary>
    public static string FormatPlatformProperty(string? platform)
    {
        var name = NormalizePlatform(platform);
        return name is null ? string.Empty : $" -p:Platform=\"{name}\"";
    }

    /// <summary>Appends <c>-c</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string Append(string arguments, string? configuration)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatSwitch(configuration);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }

    /// <summary>Appends <c>-p:Platform</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string AppendPlatform(string arguments, string? platform)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatPlatformProperty(platform);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }
}
