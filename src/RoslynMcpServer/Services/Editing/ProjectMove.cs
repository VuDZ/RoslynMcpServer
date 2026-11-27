using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Editing;

internal sealed record ProjectMove(string OldProjectPath, string NewProjectPath)
{
    internal string OldDirectory { get; } = Path.GetDirectoryName(OldProjectPath)!;
    internal string NewDirectory { get; } = Path.GetDirectoryName(NewProjectPath)!;

    internal bool Contains(string path) =>
        WorkspaceDiskPathFilter.IsPathUnderDirectory(path, OldDirectory, _pathComparison);

    internal bool IsCaseVariantOfMovingPath(string path) =>
        (string.Equals(path, OldProjectPath, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, OldProjectPath, _pathComparison))
        || (!Contains(path)
            && WorkspaceDiskPathFilter.IsPathUnderDirectory(path, OldDirectory, StringComparison.OrdinalIgnoreCase));

    internal string FinalPath(string path)
    {
        if (string.Equals(path, OldProjectPath, _pathComparison))
        {
            return NewProjectPath;
        }

        return Contains(path) ? Path.Combine(NewDirectory, Path.GetRelativePath(OldDirectory, path)) : path;
    }

    internal string? RewriteReference(string ownerPath, string reference, char separator, out bool renamedProject)
    {
        // Resolve in the original graph before calculating what the moved owner's path will resolve to.
        var oldTarget = ResolveReference(ownerPath, reference);
        renamedProject = string.Equals(oldTarget, OldProjectPath, _pathComparison);
        return FormatReferenceAfterMove(ownerPath, reference, FinalPath(oldTarget), separator, _pathComparison);
    }

    internal string? FormatReferenceAfterMove(
        string ownerPath,
        string reference,
        string targetPath,
        char separator,
        StringComparison comparison)
    {
        var newOwnerPath = FinalPath(ownerPath);
        if (string.Equals(ResolveReference(newOwnerPath, reference), targetPath, comparison))
        {
            return null;
        }

        if (reference.Contains('/') != reference.Contains('\\'))
        {
            separator = reference.Contains('/') ? '/' : '\\';
        }

        return Path.GetRelativePath(Path.GetDirectoryName(newOwnerPath)!, targetPath)
            .Replace(Path.DirectorySeparatorChar, separator);
    }

    internal static string ResolveReference(string ownerPath, string reference) =>
        Path.GetFullPath(reference.Replace('\\', '/'), Path.GetDirectoryName(ownerPath)!);

    private static readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
