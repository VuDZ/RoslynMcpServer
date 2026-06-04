using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>Resolves external assembly DLL paths for ILSpy-based MCP tools.</summary>
public static class AssemblyReferenceResolver
{
    public readonly record struct ResolveResult(bool Success, string? DllPath, string? ErrorMessage);

    /// <summary>
    /// Uses <paramref name="assemblyPath"/> when set; otherwise resolves <paramref name="assemblyName"/>
    /// against the loaded workspace metadata references.
    /// </summary>
    public static ResolveResult Resolve(Solution? solution, string? assemblyName, string? assemblyPath)
    {
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            var full = Path.GetFullPath(assemblyPath.Trim());
            if (!File.Exists(full))
            {
                return new ResolveResult(false, null, $"Error: assembly file not found: `{full}`");
            }

            return new ResolveResult(true, full, null);
        }

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return new ResolveResult(
                false,
                null,
                "Error: provide `assemblyName` (e.g. `Microsoft.TeamFoundation.Client`) or `assemblyPath` to a `.dll` file.");
        }

        if (solution is null)
        {
            return new ResolveResult(
                false,
                null,
                "Error: no active workspace. Call `load_workspace` first, or pass `assemblyPath` to a `.dll` on disk.");
        }

        var targetAssemblyName = Path.GetFileNameWithoutExtension(assemblyName.Trim());
        var dllPath = ResolveFromSolution(solution, targetAssemblyName);
        if (string.IsNullOrWhiteSpace(dllPath))
        {
            return new ResolveResult(
                false,
                null,
                $"Assembly `{targetAssemblyName}` was not found in metadata references of the loaded workspace.");
        }

        if (!File.Exists(dllPath))
        {
            return new ResolveResult(
                false,
                null,
                $"Assembly reference was resolved but file does not exist on disk: `{dllPath}`");
        }

        return new ResolveResult(true, dllPath, null);
    }

    private static string? ResolveFromSolution(Solution solution, string targetAssemblyName)
    {
        var referencePaths = solution.Projects
            .SelectMany(project => project.MetadataReferences.OfType<PortableExecutableReference>())
            .Select(reference => reference.FilePath ?? reference.Display)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return referencePaths.FirstOrDefault(path =>
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.Equals(fileName, targetAssemblyName, StringComparison.OrdinalIgnoreCase);
        }) ?? referencePaths.FirstOrDefault(path =>
            Path.GetFileName(path).Contains(targetAssemblyName, StringComparison.OrdinalIgnoreCase));
    }
}
