namespace RoslynMcpServer.Services;

/// <summary>
/// Resolves a test assembly DLL from a custom bin directory for <c>run_test_by_filter</c>.
/// </summary>
public static class TestAssemblyPathResolver
{
    public sealed record ProjectHint(string? FilePath, string? AssemblyName);

    public sealed record ResolveResult(bool Success, string? AssemblyPath, string? ErrorMessage)
    {
        public static ResolveResult Ok(string assemblyPath) => new(true, assemblyPath, null);

        public static ResolveResult Fail(string message) => new(false, null, message);
    }

    public static ResolveResult TryResolve(
        string? loadedWorkspacePath,
        string targetCsprojPath,
        string binariesDirectory,
        IEnumerable<ProjectHint> projects,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        fileExists ??= File.Exists;

        if (string.IsNullOrWhiteSpace(loadedWorkspacePath))
        {
            return ResolveResult.Fail(
                "Error: `binariesPath` requires a `.sln` or `.slnx` workspace loaded. Call `load_workspace` first.");
        }

        var loadedExt = Path.GetExtension(loadedWorkspacePath);
        if (!IsSolutionExtension(loadedExt))
        {
            return ResolveResult.Fail(
                "Error: `binariesPath` only works with a loaded `.sln`/`.slnx` workspace.");
        }

        if (string.IsNullOrWhiteSpace(targetCsprojPath)
            || !fileExists(targetCsprojPath)
            || !string.Equals(Path.GetExtension(targetCsprojPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveResult.Fail(
                "Error: `binariesPath` requires `workspacePath` to point to a `.csproj`.");
        }

        if (string.IsNullOrWhiteSpace(binariesDirectory) || !Directory.Exists(binariesDirectory))
        {
            return ResolveResult.Fail(
                $"Error: `binariesPath` is not a directory: `{binariesDirectory}`.");
        }

        ProjectHint? match = null;
        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                continue;
            }

            if (PathsEqual(project.FilePath, targetCsprojPath))
            {
                match = project;
                break;
            }
        }

        if (match is null)
        {
            return ResolveResult.Fail(
                $"Error: project `{targetCsprojPath}` was not found in the loaded workspace.");
        }

        var assemblyName = string.IsNullOrWhiteSpace(match.AssemblyName)
            ? Path.GetFileNameWithoutExtension(targetCsprojPath)
            : match.AssemblyName.Trim();
        var dllPath = Path.GetFullPath(Path.Combine(binariesDirectory, assemblyName + ".dll"));
        if (!fileExists(dllPath))
        {
            return ResolveResult.Fail($"Error: test assembly not found: `{dllPath}`.");
        }

        return ResolveResult.Ok(dllPath);
    }

    private static bool IsSolutionExtension(string? extension) =>
        string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
