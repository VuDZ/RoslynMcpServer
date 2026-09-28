using System.Text.Json;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Compile-time metadata references of a project, read from its NuGet restore assets
/// (<c>obj/project.assets.json</c>) instead of loading the project. The production build resolves a few
/// compile-only packages (<c>Microsoft.Build.Framework</c>, <c>Microsoft.Build</c>) that the test
/// process deliberately does not load, and only the restore assets know their real paths.
/// </summary>
internal static class RestoreAssets
{
    private const string AssetsRelativePath = "obj/project.assets.json";

    /// <summary>
    /// Absolute paths of the compile assets known to the restore of <paramref name="projectDirectory"/>.
    /// Missing assets are not an error here: the caller decides how to report an incomplete reference set.
    /// </summary>
    public static IReadOnlyList<string> CompileAssemblyPaths(string projectDirectory)
    {
        var assetsPath = Path.Combine(projectDirectory, AssetsRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assetsPath))
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("packageFolders", out var packageFolders)
            || !root.TryGetProperty("targets", out var targets))
        {
            return Array.Empty<string>();
        }

        var roots = packageFolders
            .EnumerateObject()
            .Select(folder => folder.Name)
            .ToList();
        var paths = new List<string>();
        foreach (var target in targets.EnumerateObject())
        {
            CollectCompileAssets(target.Value, roots, paths);
        }

        return paths;
    }

    private static void CollectCompileAssets(JsonElement target, IReadOnlyList<string> packageRoots, List<string> paths)
    {
        foreach (var package in target.EnumerateObject())
        {
            if (!package.Value.TryGetProperty("compile", out var compile))
            {
                continue;
            }

            foreach (var asset in compile.EnumerateObject())
            {
                if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var root in packageRoots)
                {
                    paths.Add(Path.Combine(
                        root,
                        package.Name.Replace('/', Path.DirectorySeparatorChar),
                        asset.Name.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
        }
    }
}
