using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Replaces the text of additional files and analyzer configs that are already documents.
/// The replacement stays on the published snapshot. <see cref="Workspace.TryApplyChanges"/> is not used:
/// it rewrites an additional file and throws for an analyzer config.
/// </summary>
internal static class WorkspaceNonCSharpDiskSync
{
    public static async Task<IReadOnlyList<KeyValuePair<string, SourceText>>> ReadUpdatesAsync(
        Solution solution,
        IReadOnlyCollection<string> paths,
        StringComparison pathComparison,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(paths);

        var updates = new List<KeyValuePair<string, SourceText>>();
        foreach (var rawPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = InputPathCanon.TryCanonicalize(rawPath);
            if (fullPath is null || !File.Exists(fullPath) || !HasDocument(solution, fullPath, pathComparison))
            {
                continue;
            }

            var encoding = SourceTextEncoding.ForDiskPath(fullPath);
            string diskText;
            try
            {
                diskText = await File.ReadAllTextAsync(fullPath, encoding, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (DocumentsMatch(solution, fullPath, diskText, pathComparison))
            {
                continue;
            }

            updates.Add(new KeyValuePair<string, SourceText>(fullPath, SourceText.From(diskText, encoding)));
        }

        return updates;
    }

    public static Solution Apply(
        Solution solution,
        IReadOnlyDictionary<string, SourceText> texts,
        StringComparison pathComparison)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return solution;
        }

        var current = solution;
        foreach (var pair in texts)
        {
            current = ApplyPath(current, pair.Key, pair.Value, pathComparison);
        }

        return current;
    }

    public static bool HasDocument(Solution solution, string fullPath, StringComparison pathComparison)
    {
        return Enumerate(solution, fullPath, pathComparison).Any();
    }

    private static Solution ApplyPath(
        Solution solution,
        string fullPath,
        SourceText text,
        StringComparison pathComparison)
    {
        var additionalIds = new List<DocumentId>();
        var configIds = new List<DocumentId>();
        foreach (var project in solution.Projects)
        {
            CollectChanged(project.AdditionalDocuments, fullPath, text, pathComparison, additionalIds);
            CollectChanged(project.AnalyzerConfigDocuments, fullPath, text, pathComparison, configIds);
        }

        var current = solution;
        foreach (var id in additionalIds)
        {
            current = current.WithAdditionalDocumentText(id, text);
        }

        foreach (var id in configIds)
        {
            current = current.WithAnalyzerConfigDocumentText(id, text);
        }

        return current;
    }

    private static void CollectChanged(
        IEnumerable<TextDocument> documents,
        string fullPath,
        SourceText text,
        StringComparison pathComparison,
        List<DocumentId> ids)
    {
        foreach (var document in documents)
        {
            if (!PathEquals(document.FilePath, fullPath, pathComparison))
            {
                continue;
            }

            if (document.TryGetText(out var existing)
                && string.Equals(existing.ToString(), text.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            ids.Add(document.Id);
        }
    }

    private static bool DocumentsMatch(
        Solution solution,
        string fullPath,
        string diskText,
        StringComparison pathComparison)
    {
        var found = false;
        foreach (var document in Enumerate(solution, fullPath, pathComparison))
        {
            found = true;
            if (!document.TryGetText(out var existing)
                || !string.Equals(existing.ToString(), diskText, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return found;
    }

    private static IEnumerable<TextDocument> Enumerate(
        Solution solution,
        string fullPath,
        StringComparison pathComparison)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.AdditionalDocuments)
            {
                if (PathEquals(document.FilePath, fullPath, pathComparison))
                {
                    yield return document;
                }
            }

            foreach (var document in project.AnalyzerConfigDocuments)
            {
                if (PathEquals(document.FilePath, fullPath, pathComparison))
                {
                    yield return document;
                }
            }
        }
    }

    private static bool PathEquals(string? documentPath, string fullPath, StringComparison pathComparison)
    {
        var canonical = InputPathCanon.TryCanonicalize(documentPath);
        return canonical is not null && string.Equals(canonical, fullPath, pathComparison);
    }
}
