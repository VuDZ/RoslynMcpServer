namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Classifies disk paths for workspace file watching. Ignores build/VCS trees so a 1000+ file
/// solution does not scan or watch <c>bin</c>/<c>obj</c>.
/// </summary>
public static class WorkspaceDiskPathFilter
{
    public static bool IsIgnoredPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (_ignoredDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCSharpSource(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        return string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProjectGraphFile(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var name = Path.GetFileName(fullPath);
        if (name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || name.Equals("global.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(fullPath);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Identifies a WPF temporary-project name; loaded-project membership is checked separately.</summary>
    public static bool IsWpfTemporaryProject(string? fullPath)
    {
        return !string.IsNullOrWhiteSpace(fullPath)
            && string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(fullPath).EndsWith("_wpftmp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathUnderDirectory(string fullFilePath, string directoryPath, StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(fullFilePath) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var fileFull = Path.GetFullPath(fullFilePath);
        var dirFull = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fileFull, dirFull, comparison))
        {
            return true;
        }

        var prefix = dirFull + Path.DirectorySeparatorChar;
        if (fileFull.StartsWith(prefix, comparison))
        {
            return true;
        }

        if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar)
        {
            var altPrefix = dirFull + Path.AltDirectorySeparatorChar;
            if (fileFull.StartsWith(altPrefix, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly StringComparer _dirNameComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> _ignoredDirectoryNames = new(_dirNameComparer)
    {
        "bin",
        "obj",
        ".git",
        "node_modules",
        "TestResults",
        "artifacts",
    };
}
