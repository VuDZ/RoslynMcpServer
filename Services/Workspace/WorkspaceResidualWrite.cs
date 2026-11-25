using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>Prepares only the changes that still need to be applied by MSBuildWorkspace.</summary>
internal static class WorkspaceResidualWrite
{
    public static async Task<(Solution Candidate, List<(Document Document, string Path, SourceText Text)> Added)>
        PrepareAsync(Solution current, Solution candidate, CancellationToken cancellationToken)
    {
        var changes = candidate.GetChanges(current);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
        {
            throw new NotSupportedException("MSBuild cannot add or remove projects through a workspace edit.");
        }

        var added = new List<(Document Document, string Path, SourceText Text)>();
        var paths = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var project in changes.GetProjectChanges())
        {
            if (project.GetAddedAdditionalDocuments().Any() || project.GetRemovedAdditionalDocuments().Any()
                || project.GetAddedAnalyzerConfigDocuments().Any() || project.GetRemovedAnalyzerConfigDocuments().Any()
                || project.GetChangedAnalyzerConfigDocuments().Any())
            {
                throw new NotSupportedException("MSBuild cannot apply these additional or analyzer configuration document changes.");
            }

            foreach (var id in project.GetAddedDocuments())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = candidate.GetDocument(id)!;
                var path = GetAddedDocumentPath(project.OldProject, document);
                if (!paths.Add(ResolveDirectoryAliases(path)) || !IsAbsent(path) || !current.GetDocumentIdsWithFilePath(path).IsEmpty)
                {
                    throw new IOException($"MSBuild cannot add a document over an existing path: {path}");
                }

                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (text.Encoding is null)
                {
                    text = SourceText.From(text.ToString(), SourceTextEncoding.BomFreeUtf8);
                }

                added.Add((document, path, text));
                candidate = candidate.RemoveDocument(id);
            }
        }

        var editedPaths = changes.GetProjectChanges().SelectMany(static project =>
            project.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true)
                .Select(id => project.NewProject.GetDocument(id)?.FilePath)
                .Concat(project.GetChangedAdditionalDocuments()
                    .Select(id => project.NewProject.GetAdditionalDocument(id)?.FilePath)))
            .OfType<string>();
        EnsureNoRemovedWritePaths(changes, editedPaths.Concat(added.Select(static item => item.Path)));

        var residual = SuppressDocumentTextChanges(current, candidate);
        if (residual.GetChanges(current).GetProjectChanges()
            .Any(change => change.GetChangedDocuments().Any() || change.GetChangedAdditionalDocuments().Any()))
        {
            throw new NotSupportedException("The MSBuild change still includes an existing document text write.");
        }

        return (residual, added);
    }

    internal static bool IsAbsent(string path) =>
        !File.Exists(path) && !Directory.Exists(path) && new FileInfo(path).LinkTarget is null;

    internal static void EnsureNoRemovedWritePaths(SolutionChanges changes, IEnumerable<string> paths)
    {
        var removed = changes.GetProjectChanges().SelectMany(static project =>
                project.GetRemovedDocuments().Select(id => project.OldProject.GetDocument(id)?.FilePath))
            .OfType<string>()
            .ToArray();
        if (removed.Length == 0)
        {
            return;
        }

        var writes = paths.Select(ResolvePhysicalFile).ToHashSet(InputPathCanon.Comparer);
        foreach (var path in removed)
        {
            if (writes.Contains(ResolvePhysicalFile(path)))
            {
                throw new IOException($"MSBuild cannot remove a document written by this edit: {path}");
            }
        }
    }

    private static Solution SuppressDocumentTextChanges(Solution current, Solution candidate)
    {
        var changes = candidate.GetChanges(current).GetProjectChanges().ToArray();
        var changed = changes.SelectMany(static project => project.GetChangedDocuments()).ToArray();
        var additional = changes.SelectMany(static project => project.GetChangedAdditionalDocuments()).ToArray();
        if (changed.Length == 0 && additional.Length == 0)
        {
            return candidate;
        }

        if (_compilationState is null || _documentState is null || _hasInfoChanged is null
            || _withDocumentState is null || _withCompilationState is null)
        {
            throw new NotSupportedException("This Roslyn version cannot isolate MSBuild project changes from existing text writes.");
        }

        var compilation = _compilationState.GetValue(candidate);
        foreach (var id in changed)
        {
            var previous = _documentState.GetValue(current.GetDocument(id)!);
            var proposed = _documentState.GetValue(candidate.GetDocument(id)!);
            if (_hasInfoChanged.Invoke(proposed, [previous]) is true)
            {
                throw new NotSupportedException("MSBuild cannot apply a change to the document's project metadata.");
            }

            compilation = _withDocumentState.Invoke(compilation, [previous]);
        }

        if (additional.Length > 0)
        {
            compilation = SuppressAdditionalDocumentTextChanges(current, candidate, additional, compilation);
        }

        return (Solution)_withCompilationState.Invoke(candidate, [compilation])!;
    }

    private static object? SuppressAdditionalDocumentTextChanges(
        Solution current,
        Solution candidate,
        IEnumerable<DocumentId> changed,
        object? compilation)
    {
        if (_textDocumentState is null || _hasInfoChanged is null || _solutionState is null
            || _withAdditionalDocumentState is null || _applyAdditionalDocumentState is null)
        {
            throw new NotSupportedException("This Roslyn version cannot isolate MSBuild additional document writes.");
        }

        foreach (var id in changed)
        {
            var previous = _textDocumentState.GetValue(current.GetAdditionalDocument(id)!);
            var document = candidate.GetAdditionalDocument(id)!;
            var proposed = _textDocumentState.GetValue(document);
            if (document.FilePath is null || _hasInfoChanged.Invoke(proposed, [previous]) is true)
            {
                throw new NotSupportedException("MSBuild cannot apply this additional document's project metadata.");
            }

            var stateChange = _withAdditionalDocumentState.Invoke(_solutionState.GetValue(compilation), [previous]);
            compilation = _applyAdditionalDocumentState.Invoke(compilation, [stateChange, id]);
        }

        return compilation;
    }

    private static string ResolvePhysicalFile(string path)
    {
        var linksLeft = OperatingSystem.IsWindows() ? 63 : 40;
        return ResolvePhysicalPath(Path.GetFullPath(path), file: true, ref linksLeft);
    }

    private static string ResolveDirectoryAliases(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var linksLeft = OperatingSystem.IsWindows() ? 63 : 40;
        var directory = ResolvePhysicalPath(Path.GetDirectoryName(fullPath)!, file: false, ref linksLeft);
        return Path.Combine(directory, Path.GetFileName(fullPath));
    }

    private static string ResolvePhysicalPath(string path, bool file, ref int linksLeft)
    {
        var root = Path.GetPathRoot(path)!;
        var physical = root;
        var segments = path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment is "." or "..")
            {
                physical = segment == ".." ? Path.GetDirectoryName(physical) ?? physical : physical;
                continue;
            }

            var entryPath = Path.Combine(physical, segment);
            var terminalFile = file && index == segments.Length - 1;
            FileSystemInfo entry = terminalFile ? new FileInfo(entryPath) : new DirectoryInfo(entryPath);
            var link = entry.LinkTarget;
            if (link is null)
            {
                physical = entryPath;
                continue;
            }

            if (linksLeft-- == 0)
            {
                throw new IOException($"Cannot resolve the filesystem link chain for {entryPath}.");
            }

            // The immediate target may itself have linked parents, even when its final component is ordinary.
            var targetPath = GetAbsoluteLinkTarget(entry, link, physical);
            physical = ResolvePhysicalPath(targetPath, terminalFile, ref linksLeft);
        }

        return physical;
    }

    private static string GetAbsoluteLinkTarget(FileSystemInfo entry, string link, string parent)
    {
        if (!Path.IsPathRooted(link))
        {
            return Path.Combine(parent, link);
        }

        return Path.IsPathFullyQualified(link)
            ? link
            : entry.ResolveLinkTarget(returnFinalTarget: false)?.FullName
                ?? throw new IOException($"Cannot resolve the filesystem link at {entry.FullName}.");
    }

    private static string GetAddedDocumentPath(Project project, Document document)
    {
        // MSBuildWorkspace derives additions from its physical project directory, logical folders and name.
        var extension = project.Language switch
        {
            LanguageNames.CSharp => ".cs",
            LanguageNames.VisualBasic => ".vb",
            _ => throw new NotSupportedException($"MSBuild cannot add a document in {project.Language}."),
        };
        var directory = Path.GetDirectoryName(project.FilePath)
            ?? throw new IOException($"MSBuild cannot locate the directory for project {project.Name}.");
        var path = Path.GetFullPath(
            Path.Combine(Path.Combine(document.Folders.ToArray()), Path.ChangeExtension(document.Name, extension)),
            directory);
        if (document.FilePath is not null
            && !InputPathCanon.Comparer.Equals(Path.GetFullPath(document.FilePath), path))
        {
            throw new IOException($"MSBuild's document path does not match the requested path: {path}");
        }

        return path;
    }

    // Roslyn merges linked members even if only a document-state identity changed. Restore the
    // original state so the project update cannot rewrite already persisted text.
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly PropertyInfo? _documentState = typeof(Document).GetProperty("DocumentState", Members);
    private static readonly PropertyInfo? _compilationState = typeof(Solution).GetProperty("CompilationState", Members);
    private static readonly MethodInfo? _hasInfoChanged = _documentState?.PropertyType.BaseType is { } baseStateType
        ? _documentState.PropertyType.GetMethod("HasInfoChanged", Members, [baseStateType])
        : null;
    private static readonly MethodInfo? _withDocumentState = _documentState?.PropertyType is { } documentStateType
        ? _compilationState?.PropertyType.GetMethod("WithDocumentState", Members, [documentStateType])
        : null;
    private static readonly MethodInfo? _withCompilationState = _compilationState?.PropertyType is { } compilationStateType
        ? typeof(Solution).GetMethod("WithCompilationState", Members, [compilationStateType])
        : null;
    private static readonly PropertyInfo? _textDocumentState = typeof(TextDocument).GetProperty("State", Members);
    private static readonly PropertyInfo? _solutionState = _compilationState?.PropertyType.GetProperty("SolutionState", Members);
    private static readonly MethodInfo? _withAdditionalDocumentState =
        typeof(TextDocument).Assembly.GetType("Microsoft.CodeAnalysis.AdditionalDocumentState") is { } additionalStateType
            ? _solutionState?.PropertyType.GetMethod("UpdateAdditionalDocumentState", Members, [additionalStateType])
            : null;
    private static readonly MethodInfo? _applyAdditionalDocumentState =
        _withAdditionalDocumentState?.ReturnType is { } stateChangeType
            ? _compilationState?.PropertyType.GetMethod("UpdateAdditionalDocumentState", Members, [stateChangeType, typeof(DocumentId)])
            : null;
}
