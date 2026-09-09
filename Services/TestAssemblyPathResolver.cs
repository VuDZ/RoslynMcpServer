namespace RoslynMcpServer.Services;

/// <summary>
/// Resolves a test assembly DLL from a custom bin directory for test tools.
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
        Func<string, bool>? fileExists = null,
        bool requireExists = true)
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

        if (string.IsNullOrWhiteSpace(binariesDirectory))
        {
            return ResolveResult.Fail("Error: `binariesPath` is empty.");
        }

        if (requireExists && !Directory.Exists(binariesDirectory))
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
        if (requireExists)
        {
            return EnsureAssemblyExists(dllPath, afterSolutionTargetBuild: false, fileExists);
        }

        return ResolveResult.Ok(dllPath);
    }

    public static ResolveResult EnsureAssemblyExists(
        string? assemblyPath,
        bool afterSolutionTargetBuild,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return ResolveResult.Fail("Error: test assembly path is empty.");
        }

        fileExists ??= File.Exists;
        var fullPath = Path.GetFullPath(assemblyPath);
        if (fileExists(fullPath))
        {
            return ResolveResult.Ok(fullPath);
        }

        return ResolveResult.Fail(FormatAssemblyNotFound(fullPath, afterSolutionTargetBuild));
    }

    internal static string FormatAssemblyNotFound(string assemblyPath, bool afterSolutionTargetBuild)
    {
        var message = $"Error: test assembly not found: `{assemblyPath}`.";
        if (afterSolutionTargetBuild)
        {
            message +=
                " Solution-target build succeeded. Check `binariesPath` and Configuration — the DLL must sit directly in that directory (with `.runtimeconfig.json` / `.deps.json`).";
        }

        return message;
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
