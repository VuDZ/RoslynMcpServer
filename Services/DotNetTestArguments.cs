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
        bool noRestore = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var args = $"test \"{targetPath}\" --logger \"console;verbosity=normal\" --verbosity normal";

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
