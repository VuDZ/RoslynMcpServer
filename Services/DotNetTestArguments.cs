namespace RoslynMcpServer.Services;

/// <summary>
/// Builds <c>dotnet test</c> argument strings for MCP test tools.
/// </summary>
public static class DotNetTestArguments
{
    public static string Build(
        string targetPath,
        string? filter = null,
        bool noBuild = false,
        bool noRestore = false,
        string? configuration = null,
        string? platform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var args = $"test \"{targetPath}\" --logger \"console;verbosity=normal\" --verbosity normal";
        args = DotNetConfigurationArguments.Append(args, configuration);
        args = DotNetConfigurationArguments.AppendPlatform(args, platform);

        if (noBuild)
        {
            args += " --no-build";
        }

        if (noRestore)
        {
            args += " --no-restore";
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            args += $" --filter \"{filter.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return args;
    }
}
