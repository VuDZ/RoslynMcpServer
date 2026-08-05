namespace RoslynMcpServer.Services;

/// <summary>
/// Formats optional MSBuild <c>-c</c> / <c>--configuration</c> for <c>dotnet build|test</c>.
/// </summary>
public static class DotNetConfigurationArguments
{
    /// <summary>
    /// Returns a leading-space fragment <c> -c "Name"</c>, or empty when <paramref name="configuration"/> is omitted.
    /// </summary>
    /// <exception cref="ArgumentException">When the name is empty after trim or contains unsafe characters.</exception>
    public static string FormatSwitch(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return string.Empty;
        }

        var name = configuration.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Configuration name is empty.", nameof(configuration));
        }

        // Keep CLI quoting safe: solution config names are like Sit-Debug / Dit-Debug.
        foreach (var ch in name)
        {
            if (ch is '"' or '\'' or '&' or '|' or ';' or '<' or '>' or '\n' or '\r' or '\0')
            {
                throw new ArgumentException(
                    $"Configuration name contains an invalid character: '{ch}'.",
                    nameof(configuration));
            }
        }

        return $" -c \"{name}\"";
    }

    /// <summary>Appends <c>-c</c> when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string Append(string arguments, string? configuration)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatSwitch(configuration);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }
}
