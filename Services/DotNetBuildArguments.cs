namespace RoslynMcpServer.Services;

/// <summary>
/// Session extra arguments appended to <c>dotnet build</c> only (probe steps and pre-test build).
/// Not applied to restore, <c>dotnet test</c>, or MSBuildWorkspace global properties.
/// </summary>
public static class DotNetBuildArguments
{
    /// <summary>
    /// Trims extra CLI args. Returns <see langword="null"/> when omitted.
    /// </summary>
    /// <exception cref="ArgumentException">When the value contains unsafe characters.</exception>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var args = value.Trim();
        if (args.Length == 0)
        {
            return null;
        }

        foreach (var ch in args)
        {
            if (ch is '"' or '\'' or '&' or '|' or ';' or '<' or '>' or '\n' or '\r' or '\0')
            {
                throw new ArgumentException(
                    $"buildArgs contains an invalid character: '{ch}'.",
                    "buildArgs");
            }
        }

        return args;
    }

    /// <summary>
    /// Returns a leading-space fragment, or empty when <paramref name="buildArgs"/> is omitted.
    /// </summary>
    public static string FormatSuffix(string? buildArgs)
    {
        var normalized = Normalize(buildArgs);
        return normalized is null ? string.Empty : $" {normalized}";
    }

    /// <summary>Appends extra args when set; otherwise returns <paramref name="arguments"/> unchanged.</summary>
    public static string Append(string arguments, string? buildArgs)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var suffix = FormatSuffix(buildArgs);
        return suffix.Length == 0 ? arguments : arguments + suffix;
    }

    public static string FormatMetadata(string? buildArgs) =>
        string.IsNullOrWhiteSpace(buildArgs) ? "(none)" : $"`{buildArgs}`";
}
