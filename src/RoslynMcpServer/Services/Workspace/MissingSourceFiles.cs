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
    /// <summary>
    /// False when the protected workspace members are not found, for example after a Roslyn update renamed them;
    /// then <see cref="TryInstallLoaders"/> changes nothing.
    /// </summary>
    public static bool IsAvailable =>
        _documentLoaderChanged is not null && _additionalDocumentLoaderChanged is not null && _analyzerConfigLoaderChanged is not null;

    /// <summary>
    /// Every document, additional document and analyzer config document whose file does not exist, except
    /// documents that share their path, ignoring case, with a document whose file exists.
    /// </summary>
    /// <remarks>
    /// Roslyn passes a changed loader to every related document, and it relates documents by path ignoring
    /// case on every platform. On a case-sensitive file system <c>Foo.cs</c> may exist while a project links
    /// a missing <c>foo.cs</c>; a loader given to <c>foo.cs</c> would make the existing <c>Foo.cs</c> empty in
    /// every tool, and a refactoring would then rewrite it from that empty text.
    /// <see cref="File.Exists(string)"/> is also false when access to the file or its folder is denied. Such a
    /// document reads as empty text instead of failing with <see cref="UnauthorizedAccessException"/>; this is
    /// an accepted difference for a file the server cannot read anyway.
    /// </remarks>
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
        var paths = documents.Select(document => document.Path).Distinct(StringComparer.Ordinal).ToArray();
        var missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Parallel.ForEach(
            paths,
            new ParallelOptions { CancellationToken = cancellationToken },
            path =>
            {
                if (!File.Exists(path))
                {
                    missing.TryAdd(path, 0);
                }
            });
        var sharedWithExistingFile = paths
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(path => !missing.ContainsKey(path)))
            .SelectMany(group => group)
            .ToHashSet(StringComparer.Ordinal);
        return documents
            .Where(document => missing.ContainsKey(document.Path) && !sharedWithExistingFile.Contains(document.Path))
            .ToArray();

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
    /// any file. Caller must not have published the workspace yet.
    /// </summary>
    /// <returns>
    /// False when the members are not available or a replacement failed; <paramref name="failure"/> then names
    /// the exception. Documents replaced before a failure keep the new loader, which gives the same empty text.
    /// </returns>
    public static bool TryInstallLoaders(
        Microsoft.CodeAnalysis.Workspace workspace,
        IReadOnlyList<MissingSourceDocument> documents,
        out string? failure)
    {
        failure = null;
        if (!IsAvailable)
        {
            return false;
        }

        try
        {
            foreach (var document in documents)
            {
                var method = document.Kind switch
                {
                    TextDocumentKind.AdditionalDocument => _additionalDocumentLoaderChanged,
                    TextDocumentKind.AnalyzerConfigDocument => _analyzerConfigLoaderChanged,
                    _ => _documentLoaderChanged,
                };
                method!.Invoke(workspace, [document.Id, new MissingSourceFileTextLoader(document.Path)]);
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or MemberAccessException)
        {
            // The load must not fail for a speedup: Roslyn's own loader still gives the same empty text, later.
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            failure = cause.GetType().FullName + ": " + cause.Message;
            return false;
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
    private static readonly MethodInfo? _documentLoaderChanged = FindLoaderChangedMethod("OnDocumentTextLoaderChanged");
    private static readonly MethodInfo? _additionalDocumentLoaderChanged = FindLoaderChangedMethod("OnAdditionalDocumentTextLoaderChanged");
    private static readonly MethodInfo? _analyzerConfigLoaderChanged = FindLoaderChangedMethod("OnAnalyzerConfigDocumentTextLoaderChanged");
}
