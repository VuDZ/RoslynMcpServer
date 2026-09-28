namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Locates the repository root from the test output directory. Production sources are analyzed from
/// the working copy, so the checks need the checkout root rather than the compiled assembly.
/// </summary>
internal static class RepositoryRoot
{
    private const string MarkerFile = "RoslynMcpServer.csproj";

    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, MarkerFile)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root ({MarkerFile} was not found above {AppContext.BaseDirectory}).");
    }

    /// <summary>Path of a repository-relative file; the separator is platform-specific, never compared as text.</summary>
    public static string Combine(string repositoryRoot, params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = repositoryRoot;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Path.Combine(parts);
    }
}
