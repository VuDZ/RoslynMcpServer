using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

public readonly record struct WorkspaceDocumentDiskSyncResult(
    Solution Solution,
    int Updated,
    int Added,
    int Removed,
    int Unchanged,
    IReadOnlyList<string> Unrepresentable);

/// <summary>
/// Syncs texts of known documents from disk into an in-memory <see cref="Solution"/> without MSBuild reopen.
/// Composition changes (new or missing <c>.cs</c> paths) are reported, not applied.
/// Only the supplied dirty paths are read, or every known document when <c>refreshAllDocuments</c> is set.
/// </summary>
public static class WorkspaceDocumentDiskSync
{
    public static async Task<WorkspaceDocumentDiskSyncResult> ApplyAsync(
        Solution solution,
        IReadOnlyCollection<string> dirtyFullPaths,
        bool refreshAllDocuments,
        StringComparison pathComparison,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var paths = new HashSet<string>(
            pathComparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        if (refreshAllDocuments)
        {
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (!string.IsNullOrWhiteSpace(document.FilePath))
                    {
                        paths.Add(Path.GetFullPath(document.FilePath));
                    }
                }
            }
        }

        if (dirtyFullPaths is not null)
        {
            foreach (var raw in dirtyFullPaths)
            {
                if (string.IsNullOrWhiteSpace(raw) || WorkspaceDiskPathFilter.IsIgnoredPath(raw))
                {
                    continue;
                }

                if (!WorkspaceDiskPathFilter.IsCSharpSource(raw))
                {
                    continue;
                }

                paths.Add(Path.GetFullPath(raw));
            }
        }

        if (paths.Count == 0)
        {
            return new WorkspaceDocumentDiskSyncResult(solution, 0, 0, 0, 0, Array.Empty<string>());
        }

        var updated = 0;
        var unchanged = 0;
        var current = solution;
        var unrepresentable = new List<string>();

        foreach (var fullPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documentId = FindDocumentIdForPath(current, fullPath, pathComparison);
            var exists = File.Exists(fullPath);

            if (documentId is not null)
            {
                if (!exists)
                {
                    unrepresentable.Add(fullPath);
                    continue;
                }

                var document = current.GetDocument(documentId);
                if (document is null)
                {
                    unchanged++;
                    continue;
                }

                var diskText = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                current = current.WithDocumentText(documentId, SourceText.From(diskText));
                if (string.Equals(sourceText.ToString(), diskText, StringComparison.Ordinal))
                {
                    unchanged++;
                }
                else
                {
                    updated++;
                }

                continue;
            }

            if (exists)
            {
                unrepresentable.Add(fullPath);
                continue;
            }

            unchanged++;
        }

        return new WorkspaceDocumentDiskSyncResult(
            current,
            updated,
            Added: 0,
            Removed: 0,
            unchanged,
            unrepresentable);
    }

    internal static DocumentId? FindDocumentIdForPath(
        Solution solution,
        string fullFilePath,
        StringComparison pathComparison)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var fp = document.FilePath;
                if (fp is not null
                    && string.Equals(Path.GetFullPath(fp), fullFilePath, pathComparison))
                {
                    return document.Id;
                }
            }
        }

        return null;
    }

    internal static Project? FindContainingProject(
        Solution solution,
        string fullFilePath,
        StringComparison pathComparison)
    {
        Project? best = null;
        var bestLength = -1;
        foreach (var project in solution.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                continue;
            }

            var projectDir = Path.GetDirectoryName(Path.GetFullPath(project.FilePath));
            if (string.IsNullOrWhiteSpace(projectDir))
            {
                continue;
            }

            if (!WorkspaceDiskPathFilter.IsPathUnderDirectory(fullFilePath, projectDir, pathComparison))
            {
                continue;
            }

            if (projectDir.Length > bestLength)
            {
                best = project;
                bestLength = projectDir.Length;
            }
        }

        return best;
    }

    internal static IReadOnlyList<string> GetDocumentFolders(
        Project project,
        string absoluteFilePath,
        StringComparison pathComparison)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return Array.Empty<string>();
        }

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(project.FilePath))!;
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(absoluteFilePath))!;
        if (!WorkspaceDiskPathFilter.IsPathUnderDirectory(fileDir, projectDir, pathComparison)
            && !string.Equals(Path.GetFullPath(fileDir), Path.GetFullPath(projectDir), pathComparison))
        {
            return Array.Empty<string>();
        }

        var relative = Path.GetRelativePath(projectDir, fileDir);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return Array.Empty<string>();
        }

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
