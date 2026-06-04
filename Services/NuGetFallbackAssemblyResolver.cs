using System.Collections.Concurrent;
using ICSharpCode.Decompiler.Metadata;

namespace RoslynMcpServer.Services;

/// <summary>
/// Wraps <see cref="UniversalAssemblyResolver"/> and falls back to scanning the NuGet global packages folder.
/// </summary>
internal sealed class NuGetFallbackAssemblyResolver : IAssemblyResolver
{
    private static readonly ConcurrentDictionary<string, string?> NuGetDllCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly UniversalAssemblyResolver _inner;
    private readonly string _nugetPackagesRoot;

    public NuGetFallbackAssemblyResolver(UniversalAssemblyResolver inner, string nugetPackagesRoot)
    {
        _inner = inner;
        _nugetPackagesRoot = nugetPackagesRoot;
    }

    public MetadataFile Resolve(IAssemblyReference reference)
    {
        if (ShouldPreferNuGetFallback(reference))
        {
            var early = TryFindAssemblyDll(reference.Name);
            if (early is not null)
            {
                return new PEFile(early);
            }
        }

        try
        {
            return _inner.Resolve(reference);
        }
        catch (ResolutionException)
        {
            var path = TryFindAssemblyDll(reference.Name);
            if (path is not null)
            {
                return new PEFile(path);
            }

            throw;
        }
    }

    public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) =>
        Task.FromResult<MetadataFile?>(Resolve(reference));

    public MetadataFile ResolveModule(MetadataFile module, string fileName) =>
        _inner.ResolveModule(module, fileName);

    public Task<MetadataFile?> ResolveModuleAsync(MetadataFile module, string fileName) =>
        _inner.ResolveModuleAsync(module, fileName);

    internal static bool ShouldPreferNuGetFallback(IAssemblyReference reference)
    {
        var version = reference.Version;
        return version is null || (version.Major == 0 && version.Minor == 0 && version.Build == 0);
    }

    internal static string? TryFindAssemblyDll(string assemblyName, string? nugetRoot = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var root = nugetRoot ?? GetNuGetPackagesRoot();
        return NuGetDllCache.GetOrAdd($"{root}|{assemblyName}", _ => ScanNuGetForAssembly(root, assemblyName));
    }

    private string? TryFindAssemblyDll(string assemblyName) => TryFindAssemblyDll(assemblyName, _nugetPackagesRoot);

    private static string? ScanNuGetForAssembly(string nugetRoot, string assemblyName)
    {
        if (!Directory.Exists(nugetRoot))
        {
            return null;
        }

        var fileName = assemblyName + ".dll";

        if (BclAssemblyPackageMap.TryGetPackageId(assemblyName, out var packageId))
        {
            var mappedDir = Path.Combine(nugetRoot, packageId);
            if (Directory.Exists(mappedDir))
            {
                var fromMapped = PickBestDll(Directory.EnumerateFiles(mappedDir, fileName, SearchOption.AllDirectories));
                if (fromMapped is not null)
                {
                    return fromMapped;
                }
            }
        }

        var packageIdDir = Path.Combine(nugetRoot, assemblyName.ToLowerInvariant());
        if (Directory.Exists(packageIdDir))
        {
            var fromPackage = PickBestDll(Directory.EnumerateFiles(packageIdDir, fileName, SearchOption.AllDirectories));
            if (fromPackage is not null)
            {
                return fromPackage;
            }
        }

        string? best = null;
        var bestScore = int.MinValue;
        foreach (var dll in Directory.EnumerateFiles(nugetRoot, fileName, SearchOption.AllDirectories))
        {
            var score = ScoreTfMPath(dll);
            if (score > bestScore)
            {
                bestScore = score;
                best = dll;
            }
        }

        return best;
    }

    private static string? PickBestDll(IEnumerable<string> paths)
    {
        string? best = null;
        var bestScore = int.MinValue;
        foreach (var dll in paths)
        {
            var score = ScoreTfMPath(dll);
            if (score > bestScore)
            {
                bestScore = score;
                best = dll;
            }
        }

        return best;
    }

    internal static int ScoreTfMPath(string dllPath)
    {
        var path = dllPath.Replace('\\', '/');
        if (OperatingSystem.IsWindows()
            && path.Contains("/runtimes/", StringComparison.OrdinalIgnoreCase)
            && (path.Contains("/unix/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/linux/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/osx/", StringComparison.OrdinalIgnoreCase)))
        {
            return -1000;
        }

        string[] preferred =
        [
            "net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0", "net472", "net462"
        ];

        for (var i = 0; i < preferred.Length; i++)
        {
            if (path.Contains($"/{preferred[i]}/", StringComparison.OrdinalIgnoreCase))
            {
                return preferred.Length - i;
            }
        }

        return 0;
    }

    private static string GetNuGetPackagesRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv.Trim();
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }
}
