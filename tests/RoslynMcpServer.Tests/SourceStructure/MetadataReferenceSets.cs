using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Metadata references for an in-memory compilation. The runtime set is the assembly list of the
/// running test process; the production set adds the compile-only assets of the restore, which the
/// test process does not load. Assemblies of the solution itself are excluded: the analyzed types must
/// come from the sources under check, not from the built binary.
/// </summary>
internal static class MetadataReferenceSets
{
    private const string SolutionAssemblyPrefix = "RoslynMcpServer";

    /// <summary>Framework and NuGet assemblies the test process itself resolved, without solution assemblies.</summary>
    public static IReadOnlyList<MetadataReference> Runtime() => _runtime.Value;

    /// <summary>
    /// <see cref="Runtime"/> plus the compile assets of <paramref name="projectDirectory"/> that the
    /// runtime set does not contain. The runtime entry wins on a simple-name clash, so the reference
    /// set stays free of duplicate assembly identities.
    /// </summary>
    public static IReadOnlyList<MetadataReference> Production(string projectDirectory)
    {
        lock (_productionGate)
        {
            if (_productionByProject.TryGetValue(projectDirectory, out var cached))
            {
                return cached;
            }

            var bySimpleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in TrustedPlatformAssemblyPaths())
            {
                bySimpleName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }

            foreach (var path in RestoreAssets.CompileAssemblyPaths(projectDirectory))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (IsSolutionAssembly(name) || !File.Exists(path))
                {
                    continue;
                }

                bySimpleName.TryAdd(name, path);
            }

            var references = CreateReferences(bySimpleName.Values);
            _productionByProject[projectDirectory] = references;
            return references;
        }
    }

    private static IReadOnlyList<MetadataReference> CreateRuntime()
    {
        var bySimpleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in TrustedPlatformAssemblyPaths())
        {
            bySimpleName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        return CreateReferences(bySimpleName.Values);
    }

    private static IReadOnlyList<MetadataReference> CreateReferences(IEnumerable<string> paths)
    {
        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static IEnumerable<string> TrustedPlatformAssemblyPaths()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(trusted))
        {
            return Array.Empty<string>();
        }

        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !IsSolutionAssembly(Path.GetFileNameWithoutExtension(path)));
    }

    private static bool IsSolutionAssembly(string simpleName) =>
        simpleName.StartsWith(SolutionAssemblyPrefix, StringComparison.OrdinalIgnoreCase);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> _runtime =
        new(CreateRuntime, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Dictionary<string, IReadOnlyList<MetadataReference>> _productionByProject =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock _productionGate = new();
}
