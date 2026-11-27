using System.Text.Json;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Exact roots whose content is treated as immutable for disk watching: the NuGet package folders listed by
/// <c>project.assets.json</c>, and <c>packs</c>, <c>shared</c> and <c>sdk</c> of the dotnet root of the
/// registered MSBuild. A package folder is keyed by id and version, and restore does not rewrite an existing one.
/// </summary>
/// <remarks>
/// See docs/disk-watcher-inotify/README.md (O-01, O-02). Path patterns such as <c>/packs/</c> are not used:
/// they would also match a user directory with that name and silently stop watching it. A root that cannot be
/// read is simply absent, so its inputs stay watched.
/// </remarks>
internal static class ImmutableInputRoots
{
    public static IReadOnlyList<string> Discover(IEnumerable<string?> projectFilePaths, string? msBuildPath)
    {
        ArgumentNullException.ThrowIfNull(projectFilePaths);

        var roots = new HashSet<string>(InputPathCanon.Comparer);
        var readAssets = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var projectFilePath in projectFilePaths)
        {
            var directory = Path.GetDirectoryName(projectFilePath);
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var assetsPath = Path.Combine(directory, "obj", "project.assets.json");
            if (!readAssets.Add(assetsPath))
            {
                continue;
            }

            foreach (var folder in ReadPackageFolders(assetsPath))
            {
                AddCanonical(roots, folder);
            }
        }

        foreach (var root in DotNetRoots(msBuildPath))
        {
            AddCanonical(roots, root);
        }

        return roots.ToArray();
    }

    /// <summary>True when <paramref name="canonicalPath"/> is a root itself or lies under one.</summary>
    public static bool Contains(IReadOnlyList<string> roots, string canonicalPath) =>
        roots.Any(root => WorkspaceDiskPathFilter.IsPathUnderDirectory(canonicalPath, root, InputPathCanon.Comparison));

    /// <summary>
    /// A dotnet SDK instance registers <c>&lt;dotnet&gt;/sdk/&lt;version&gt;</c> as its MSBuild path. Any other
    /// layout, such as Visual Studio MSBuild, gives no root.
    /// </summary>
    internal static IReadOnlyList<string> DotNetRoots(string? msBuildPath)
    {
        if (string.IsNullOrWhiteSpace(msBuildPath))
        {
            return Array.Empty<string>();
        }

        var sdkDirectory = Path.GetDirectoryName(msBuildPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (sdkDirectory is null || !string.Equals(Path.GetFileName(sdkDirectory), "sdk", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var dotnetRoot = Path.GetDirectoryName(sdkDirectory);
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            return Array.Empty<string>();
        }

        return [sdkDirectory, Path.Combine(dotnetRoot, "packs"), Path.Combine(dotnetRoot, "shared")];
    }

    /// <summary>Reads the names of the top-level <c>packageFolders</c> object; any read or format error yields none.</summary>
    internal static IReadOnlyList<string> ReadPackageFolders(string assetsPath)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(assetsPath))
            {
                return Array.Empty<string>();
            }

            bytes = File.ReadAllBytes(assetsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        try
        {
            return ReadPackageFolders(bytes);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ReadPackageFolders(byte[] bytes)
    {
        // project.assets.json can be several megabytes; the other top-level values are skipped, not materialized.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return Array.Empty<string>();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isPackageFolders = reader.ValueTextEquals("packageFolders");
            reader.Read();
            if (!isPackageFolders)
            {
                reader.Skip();
                continue;
            }

            return reader.TokenType == JsonTokenType.StartObject
                ? ReadPropertyNames(ref reader)
                : Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    private static List<string> ReadPropertyNames(ref Utf8JsonReader reader)
    {
        var names = new List<string>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }

            reader.Read();
            reader.Skip();
        }

        return names;
    }

    private static void AddCanonical(HashSet<string> roots, string path)
    {
        // A relative or malformed root must not be resolved against the process working directory.
        if (!Path.IsPathFullyQualified(path))
        {
            return;
        }

        // packageFolders keys end with a separator; trim it so the same folder from several projects is one root.
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is not null)
        {
            roots.Add(Path.TrimEndingDirectorySeparator(canonical));
        }
    }
}
