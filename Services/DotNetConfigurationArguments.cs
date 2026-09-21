namespace RoslynMcpServer.Services;

/// <summary>
/// Formats optional MSBuild <c>-c</c> / <c>-p:Configuration</c> / <c>-p:Platform</c>
/// for <c>dotnet build|test</c> and sanitizes names used as MSBuildWorkspace global properties.
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
    /// Use for MSBuildWorkspace global properties and SDK-style <c>.csproj</c> CLI.
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

    /// <summary>
    /// <see langword="true"/> when <paramref name="targetPath"/> is a <c>.sln</c> / <c>.slnx</c>
    /// (solution configuration names are verbatim, e.g. <c>Any CPU</c>).
    /// </summary>
    public static bool UsesSolutionPlatformNaming(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var extension = Path.GetExtension(targetPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CLI platform for <paramref name="targetPath"/>: trim+validate for <c>.sln</c>/<c>.slnx</c>;
    /// <see cref="NormalizePlatform"/> for <c>.csproj</c> and unknown targets.
    /// </summary>
    public static string? NormalizePlatformForTarget(string? platform, string? targetPath) =>
        UsesSolutionPlatformNaming(targetPath)
            ? Normalize(platform, nameof(platform))
            : NormalizePlatform(platform);

    public static string? Coalesce(string? explicitValue, string? cached, string paramName) =>
        Normalize(explicitValue, paramName) ?? cached;

    /// <summary>
    /// Always-canonical coalesce (workspace-style). Prefer
    /// <see cref="CoalescePlatformForTarget"/> for <c>dotnet build|test</c>.
    /// </summary>
    public static string? CoalescePlatform(string? explicitValue, string? cached) =>
        NormalizePlatform(explicitValue) ?? cached;

    /// <summary>
    /// Inherits platform for a concrete CLI target: explicit value is normalized for that target;
    /// when omitted, uses <paramref name="cachedRaw"/> for <c>.sln</c>/<c>.slnx</c> and
    /// <paramref name="cachedCanonical"/> for <c>.csproj</c>.
    /// </summary>
    public static string? CoalescePlatformForTarget(
        string? explicitValue,
        string? cachedRaw,
        string? cachedCanonical,
        string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return NormalizePlatformForTarget(explicitValue, targetPath);
        }

        return UsesSolutionPlatformNaming(targetPath) ? cachedRaw : cachedCanonical;
    }

    /// <summary>
    /// Returns a leading-space fragment <c> -c "Name"</c>, or empty when <paramref name="configuration"/> is omitted.
    /// Use for <c>dotnet test</c> so the CLI still locates <c>bin/{configuration}</c>.
    /// </summary>
    public static string FormatSwitch(string? configuration)
    {
        var name = Normalize(configuration, nameof(configuration));
        return name is null ? string.Empty : $" -c \"{name}\"";
    }

    /// <summary>
    /// Returns a leading-space fragment <c> -p:Configuration="Name"</c>, or empty when omitted.
    /// Use for <c>dotnet build</c> (probe and pre-test compile).
    /// </summary>
    public static string FormatConfigurationProperty(string? configuration)
    {
        var name = Normalize(configuration, nameof(configuration));
        return name is null ? string.Empty : $" -p:Configuration=\"{name}\"";
    }

    /// <summary>
    /// Returns a leading-space fragment <c> -p:Platform="Name"</c>, or empty when omitted.
    /// When <paramref name="targetPath"/> is a <c>.sln</c>/<c>.slnx</c>, keeps <c>Any CPU</c> verbatim;
    /// otherwise aliases to <c>AnyCPU</c> (SDK <c>.csproj</c> / unknown).
    /// </summary>
    public static string FormatPlatformProperty(string? platform, string? targetPath = null)
    {
        var name = NormalizePlatformForTarget(platform, targetPath);
        return name is null ? string.Empty : $" -p:Platform=\"{name}\"";
    }

    /// <summary>Appends <c>-c</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string Append(string arguments, string? configuration)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatSwitch(configuration);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }

    /// <summary>Appends <c>-p:Configuration</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string AppendConfigurationProperty(string arguments, string? configuration)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatConfigurationProperty(configuration);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }

    /// <summary>Appends <c>-p:Platform</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string AppendPlatform(string arguments, string? platform, string? targetPath = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatPlatformProperty(platform, targetPath);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }
}
