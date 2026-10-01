using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services.Workspace;

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
    public static Task<WorkspaceDocumentDiskSyncResult> ApplyAsync(
        Solution solution,
        IReadOnlyCollection<string> dirtyFullPaths,
        bool refreshAllDocuments,
        StringComparison pathComparison,
        CancellationToken cancellationToken = default)
    {
        return ApplyAsync(solution, dirtyFullPaths, refreshAllDocuments, pathComparison, inputMap: null, cancellationToken);
    }

    internal static async Task<WorkspaceDocumentDiskSyncResult> ApplyAsync(
        Solution solution,
        IReadOnlyCollection<string> dirtyFullPaths,
        bool refreshAllDocuments,
        StringComparison pathComparison,
        WorkspaceInputMap? inputMap,
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
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (WorkspaceDiskPathFilter.IsIgnoredPath(raw)
                    && !WorkspaceDiskEventClassifier.IsAddressedUserInput(inputMap, raw))
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
            var documentIds = FindUserDocumentIdsForPath(current, fullPath, pathComparison, inputMap);
            var exists = File.Exists(fullPath);
            if (documentIds.Count == 0)
            {
                if (exists && inputMap?.Find(fullPath) is null)
                {
                    unrepresentable.Add(fullPath);
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            if (!exists)
            {
                unrepresentable.Add(fullPath);
                continue;
            }

            var applied = await ApplyConfirmedTextAsync(current, fullPath, documentIds, cancellationToken)
                .ConfigureAwait(false);
            current = applied.Solution;
            updated += applied.Updated;
            unchanged += applied.Unchanged;
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
        var ids = FindDocumentIdsForPath(solution, fullFilePath, pathComparison);
        return ids.Count == 0 ? null : ids[0];
    }

    internal static IReadOnlyList<DocumentId> FindDocumentIdsForPath(
        Solution solution,
        string fullFilePath,
        StringComparison pathComparison)
    {
        var ids = new List<DocumentId>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var fp = document.FilePath;
                if (fp is not null
                    && string.Equals(Path.GetFullPath(fp), fullFilePath, pathComparison))
                {
                    ids.Add(document.Id);
                }
            }
        }

        return ids;
    }

    private static IReadOnlyList<DocumentId> FindUserDocumentIdsForPath(
        Solution solution,
        string fullPath,
        StringComparison pathComparison,
        WorkspaceInputMap? inputMap)
    {
        var ids = new List<DocumentId>();
        foreach (var documentId in FindDocumentIdsForPath(solution, fullPath, pathComparison))
        {
            if (WorkspaceDiskEventClassifier.SyncsUserText(inputMap, fullPath, documentId))
            {
                ids.Add(documentId);
            }
        }

        return ids;
    }

    private static async Task<(Solution Solution, int Updated, int Unchanged)> ApplyConfirmedTextAsync(
        Solution current,
        string fullPath,
        IReadOnlyList<DocumentId> documentIds,
        CancellationToken cancellationToken)
    {
        // One read, one SourceText. Every membership of this path gets that text, so a linked
        // file is not published as a different string per project and the BOM state is the file's.
        var encoding = SourceTextEncoding.ForDiskPath(fullPath);
        var diskText = await File.ReadAllTextAsync(fullPath, encoding, cancellationToken).ConfigureAwait(false);
        var shared = SourceText.From(diskText, encoding);
        var updated = 0;
        var unchanged = 0;
        foreach (var documentId in documentIds)
        {
            var document = current.GetDocument(documentId);
            if (document is null)
            {
                unchanged++;
                continue;
            }

            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(sourceText.ToString(), diskText, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            current = current.WithDocumentText(documentId, shared);
            updated++;
        }

        return (current, updated, unchanged);
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
