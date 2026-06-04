using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>Resolves assembly DLL paths from project <c>bin/**.deps.json</c> runtime graphs (exact file name match).</summary>
internal static class DepsJsonAssemblyPathResolver
{
    public static string? TryResolveFromSolution(Solution solution, string targetAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(targetAssemblyName))
        {
            return null;
        }

        var dllFileName = targetAssemblyName + ".dll";
        var nugetRoot = NuGetPackagesRoot.Get();
        string? bestPath = null;
        var bestScore = int.MinValue;
        var bestDepsTime = DateTime.MinValue;

        foreach (var project in solution.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                continue;
            }

            var projectDir = Path.GetDirectoryName(project.FilePath);
            if (projectDir is null)
            {
                continue;
            }

            var binDir = Path.Combine(projectDir, "bin");
            if (!Directory.Exists(binDir))
            {
                continue;
            }

            foreach (var depsPath in Directory.EnumerateFiles(binDir, "*.deps.json", SearchOption.AllDirectories))
            {
                var depsTime = File.GetLastWriteTimeUtc(depsPath);
                if (!TryResolveFromDepsFile(depsPath, dllFileName, nugetRoot, out var resolved, out var score))
                {
                    continue;
                }

                if (score > bestScore || (score == bestScore && depsTime > bestDepsTime))
                {
                    bestScore = score;
                    bestDepsTime = depsTime;
                    bestPath = resolved;
                }
            }
        }

        return bestPath;
    }

    internal static bool TryResolveFromDepsFile(
        string depsJsonPath,
        string dllFileName,
        string nugetRoot,
        out string? dllPath,
        out int tfmScore)
    {
        dllPath = null;
        tfmScore = int.MinValue;

        if (!File.Exists(depsJsonPath) || string.IsNullOrWhiteSpace(dllFileName))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(depsJsonPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("targets", out var targets)
                || !root.TryGetProperty("libraries", out var libraries))
            {
                return false;
            }

            string? bestPackageKey = null;
            string? bestRelativePath = null;
            var bestLocalScore = int.MinValue;

            foreach (var targetFramework in targets.EnumerateObject())
            {
                foreach (var package in targetFramework.Value.EnumerateObject())
                {
                    if (!TryFindRuntimeDll(package.Value, dllFileName, out var relativePath, out var localScore))
                    {
                        continue;
                    }

                    if (localScore > bestLocalScore)
                    {
                        bestLocalScore = localScore;
                        bestPackageKey = package.Name;
                        bestRelativePath = relativePath;
                    }
                }
            }

            if (bestPackageKey is null || bestRelativePath is null)
            {
                return false;
            }

            if (!libraries.TryGetProperty(bestPackageKey, out var library)
                || !library.TryGetProperty("path", out var pathElement))
            {
                return false;
            }

            var packageFolder = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(packageFolder))
            {
                return false;
            }

            var candidate = Path.Combine(
                nugetRoot,
                packageFolder.Replace('/', Path.DirectorySeparatorChar),
                bestRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(candidate))
            {
                return false;
            }

            dllPath = candidate;
            tfmScore = bestLocalScore;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryFindRuntimeDll(
        JsonElement packageNode,
        string dllFileName,
        out string? relativePath,
        out int score)
    {
        relativePath = null;
        score = int.MinValue;

        if (TryFindInRuntimeSection(packageNode, "runtime", dllFileName, out relativePath, out score))
        {
            return true;
        }

        return TryFindInRuntimeSection(packageNode, "runtimeTargets", dllFileName, out relativePath, out score);
    }

    private static bool TryFindInRuntimeSection(
        JsonElement packageNode,
        string sectionName,
        string dllFileName,
        out string? relativePath,
        out int score)
    {
        relativePath = null;
        score = int.MinValue;

        if (!packageNode.TryGetProperty(sectionName, out var runtime))
        {
            return false;
        }

        foreach (var entry in runtime.EnumerateObject())
        {
            if (!string.Equals(Path.GetFileName(entry.Name), dllFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryScore = NuGetFallbackAssemblyResolver.ScoreTfMPath(entry.Name);
            if (entryScore > score)
            {
                score = entryScore;
                relativePath = entry.Name;
            }
        }

        return relativePath is not null;
    }
}

internal static class NuGetPackagesRoot
{
    public static string Get()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv.Trim();
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }
}
