namespace RoslynMcpServer.Services;

/// <summary>Resolves repo/solution roots for <c>dotnet</c> CLI (global.json, solution layout).</summary>
public static class WorkspaceRootResolver
{
    /// <summary>
    /// Directory to use as <c>WorkingDirectory</c> for <c>dotnet build|test|restore</c>:
    /// nearest ancestor containing <c>global.json</c>, else the folder holding the .sln/.slnx/.csproj.
    /// </summary>
    public static string ResolveDotNetWorkingDirectory(string solutionOrProjectPath)
    {
        var fullPath = Path.GetFullPath(solutionOrProjectPath);
        var startDir = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath)
            : fullPath;

        if (string.IsNullOrEmpty(startDir))
        {
            return Environment.CurrentDirectory;
        }

        var globalJsonDir = FindDirectoryContainingGlobalJson(startDir);
        return globalJsonDir ?? startDir;
    }

    public static string? FindSolutionOrProjectInDirectory(string directory)
    {
        var fullDir = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDir))
        {
            return null;
        }

        var sln = Directory.EnumerateFiles(fullDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (sln is not null)
        {
            return sln;
        }

        var slnx = Directory.EnumerateFiles(fullDir, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (slnx is not null)
        {
            return slnx;
        }

        return Directory.EnumerateFiles(fullDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    public static string? FindDirectoryContainingGlobalJson(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
