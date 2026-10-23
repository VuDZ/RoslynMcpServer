using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Finds workspace documents whose files are missing and gives them <see cref="MissingSourceFileTextLoader"/>
/// in the workspace itself, before any snapshot is published.
/// </summary>
internal static class MissingSourceFiles
{
    /// <summary>Every document, additional document and analyzer config document whose file does not exist.</summary>
    public static IReadOnlyList<MissingSourceDocument> Find(Solution solution, CancellationToken cancellationToken)
    {
        var documents = new List<MissingSourceDocument>();
        foreach (var project in solution.Projects)
        {
            Add(project.Documents, TextDocumentKind.Document);
            Add(project.AdditionalDocuments, TextDocumentKind.AdditionalDocument);
            Add(project.AnalyzerConfigDocuments, TextDocumentKind.AnalyzerConfigDocument);
        }

        // A linked file is one path in many projects: check each path once, in parallel, since a large
        // solution has tens of thousands of files and every check is a separate file system call.
        var missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Parallel.ForEach(
            documents.Select(document => document.Path).Distinct(StringComparer.Ordinal),
            new ParallelOptions { CancellationToken = cancellationToken },
            path =>
            {
                if (!File.Exists(path))
                {
                    missing.TryAdd(path, 0);
                }
            });
        return documents.Where(document => missing.ContainsKey(document.Path)).ToArray();

        void Add(IEnumerable<TextDocument> items, TextDocumentKind kind)
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    documents.Add(new MissingSourceDocument(item.Id, kind, item.FilePath));
                }
            }
        }
    }

    /// <summary>
    /// Replaces the loaders of <paramref name="documents"/> in <paramref name="workspace"/> without writing
    /// any file.
    /// </summary>
    /// <returns>False when the protected workspace members are not found; the documents keep their loaders.</returns>
    public static bool TryInstallLoaders(
        Microsoft.CodeAnalysis.Workspace workspace,
        IReadOnlyList<MissingSourceDocument> documents)
    {
        if (DocumentLoaderChanged is null || AdditionalDocumentLoaderChanged is null || AnalyzerConfigLoaderChanged is null)
        {
            return false;
        }

        foreach (var document in documents)
        {
            var method = document.Kind switch
            {
                TextDocumentKind.AdditionalDocument => AdditionalDocumentLoaderChanged,
                TextDocumentKind.AnalyzerConfigDocument => AnalyzerConfigLoaderChanged,
                _ => DocumentLoaderChanged,
            };
            method.Invoke(workspace, [document.Id, new MissingSourceFileTextLoader(document.Path)]);
        }

        return true;
    }

    private static MethodInfo? FindLoaderChangedMethod(string name) =>
        typeof(Microsoft.CodeAnalysis.Workspace).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(DocumentId), typeof(TextLoader)]);

    // The loader must change in the workspace itself. A published snapshot that differed from the workspace
    // only by its loader would reach TryApplyChanges as a text change of that document, and MSBuildWorkspace
    // would write the empty text to disk. MSBuildWorkspace has no accessible constructor to subclass, so these
    // protected members of Workspace, the way a workspace changes a loader without writing, are called by name.
    private static readonly MethodInfo? DocumentLoaderChanged = FindLoaderChangedMethod("OnDocumentTextLoaderChanged");
    private static readonly MethodInfo? AdditionalDocumentLoaderChanged = FindLoaderChangedMethod("OnAdditionalDocumentTextLoaderChanged");
    private static readonly MethodInfo? AnalyzerConfigLoaderChanged = FindLoaderChangedMethod("OnAnalyzerConfigDocumentTextLoaderChanged");
}
