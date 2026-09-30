namespace RoslynMcpServer.Services;

/// <summary>
/// Reads declared <c>TargetFramework</c> / <c>TargetFrameworks</c> text from the project file and
/// ancestor <c>Directory.Build.props</c>. This is a completeness signal only: it does not evaluate
/// conditions or items, and a longer declared list than the loaded project objects is unconfirmed coverage.
/// </summary>
internal static class DeclaredTargetFrameworkEvidence
{
    public static IReadOnlyList<string> Read(string? projectFilePath)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(found, seen, ReadProjectFile(projectFilePath));
        if (!string.IsNullOrWhiteSpace(projectFilePath))
        {
            Add(found, seen, DirectoryBuildPropsReader.ListTargetFrameworks(projectFilePath));
        }

        return found;
    }

    private static IReadOnlyList<string> ReadProjectFile(string? projectFilePath)
    {
        var canonical = InputPathCanon.TryCanonicalize(projectFilePath);
        if (canonical is null || !File.Exists(canonical))
        {
            return Array.Empty<string>();
        }

        try
        {
            return DirectoryBuildPropsReader.Parse(File.ReadAllText(canonical));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static void Add(List<string> found, HashSet<string> seen, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            if (value.Length == 0 || !seen.Add(value))
            {
                continue;
            }

            found.Add(value);
        }
    }
}
