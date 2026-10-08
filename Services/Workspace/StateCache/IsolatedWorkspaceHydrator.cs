using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Restores the accepted Debug base-query profile through public APIs. The input is not a cache-validity
/// proof. Current source is checked for H-002 independence before any candidate is returned.
/// </summary>
internal sealed class IsolatedWorkspaceHydrator
{
    /// <summary>Optional internal resource seams support deterministic ownership tests, not production admission.</summary>
    internal IsolatedWorkspaceHydrator(
        Func<string, CancellationToken, Task<byte[]>>? readBytes = null,
        Action<AdhocWorkspace>? workspaceDisposed = null,
        Action<Metadata>? metadataCreated = null)
    {
        _readBytes = readBytes;
        _workspaceDisposed = workspaceDisposed;
        _metadataCreated = metadataCreated;
    }

    public async Task<IsolatedHydratedSession> HydrateAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The codec is the single schema validator. Its round-trip also detaches mutable DTO arrays
        // from the caller before asynchronous I/O, so a changed array cannot bypass those checks.
        var frozen = WorkspaceSnapshotCodec.Decode(WorkspaceSnapshotCodec.Encode(snapshot));
        var workspace = new AdhocWorkspace();
        var metadata = new List<Metadata>();
        try
        {
            // Codec keys use the platform's path identity. Default record equality would reject
            // a valid edge/owner/producer that spells the same physical project path differently.
            var ids = frozen.Projects.ToDictionary(
                project => project.Key, _ => ProjectId.CreateNewId(), WorkspaceSnapshotCodec.ProjectKeyIdentity);
            var texts = await ReadDocumentsAsync(frozen, cancellationToken);
            var references = await ReadMetadataAsync(frozen, metadata, cancellationToken);
            var solution = RestoreSolution(frozen, workspace, ids, texts, references, cancellationToken);
            var compilations = await CreateBaseCompilationsAsync(solution, frozen, ids, cancellationToken);
            var inputMap = RebindInputs(frozen, solution, ids);
            cancellationToken.ThrowIfCancellationRequested();
            return new IsolatedHydratedSession(workspace, solution, ids, compilations, inputMap, metadata, _workspaceDisposed);
        }
        catch
        {
            try
            {
                workspace.Dispose();
            }
            finally
            {
                foreach (var item in metadata)
                {
                    item.Dispose();
                }
            }
            _workspaceDisposed?.Invoke(workspace);
            throw;
        }
    }

    private async Task<Dictionary<(SnapshotProjectKey Owner, string Kind, string Path), SourceText>> ReadDocumentsAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var bytesByPath = new Dictionary<string, byte[]>(InputPathCanon.Comparer);
        var texts = new Dictionary<(SnapshotProjectKey Owner, string Kind, string Path), SourceText>();
        foreach (var project in snapshot.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!bytesByPath.TryGetValue(document.Path, out var bytes))
                {
                    bytes = await ReadBytesAsync(document.Path, cancellationToken);
                    bytesByPath.Add(document.Path, bytes);
                }
                var text = DecodeText(bytes, document.TextMetadata);
                texts.Add((project.Key, document.Kind, document.Path), text);
            }
        }
        return texts;
    }

    private async Task<Dictionary<(string Path, MetadataImageKind Kind), Metadata>> ReadMetadataAsync(
        WorkspaceSnapshot snapshot,
        List<Metadata> owned,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string Path, MetadataImageKind Kind), Metadata>();
        foreach (var reference in snapshot.Projects.SelectMany(project => project.MetadataReferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = Enum.Parse<MetadataImageKind>(reference.Kind);
            var key = (reference.Path, kind);
            if (result.ContainsKey(key))
            {
                continue;
            }
            var bytes = await ReadBytesAsync(reference.Path, cancellationToken);
            Metadata image = kind == MetadataImageKind.Assembly
                ? AssemblyMetadata.CreateFromImage(bytes.ToImmutableArray())
                : ModuleMetadata.CreateFromImage(bytes.ToImmutableArray());
            owned.Add(image);
            _metadataCreated?.Invoke(image);
            ValidateImage(image);
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(key, image);
        }
        return result;
    }

    private static void ValidateImage(Metadata image)
    {
        var modules = image is AssemblyMetadata assembly ? assembly.GetModules() : [(ModuleMetadata)image];
        foreach (var module in modules)
        {
            var reader = module.GetMetadataReader();
            // The byte-image owner has no module-file resolver. Refuse an assembly that requires
            // additional module images instead of constructing a reference with missing state.
            if (reader.AssemblyFiles.Any(handle => reader.GetAssemblyFile(handle).ContainsMetadata))
            {
                throw new InvalidDataException("The base-query profile excludes external assembly modules.");
            }
        }
    }

    private static Solution RestoreSolution(
        WorkspaceSnapshot snapshot,
        AdhocWorkspace workspace,
        IReadOnlyDictionary<SnapshotProjectKey, ProjectId> ids,
        IReadOnlyDictionary<(SnapshotProjectKey Owner, string Kind, string Path), SourceText> texts,
        IReadOnlyDictionary<(string Path, MetadataImageKind Kind), Metadata> metadata,
        CancellationToken cancellationToken)
    {
        var solution = workspace.CurrentSolution;
        foreach (var project in snapshot.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ids[project.Key];
            var documents = project.Documents.Select(document => DocumentInfo.Create(
                DocumentId.CreateNewId(id),
                document.Name,
                document.Folders,
                Enum.Parse<SourceCodeKind>(document.SourceCodeKind),
                TextLoader.From(TextAndVersion.Create(
                    texts[(project.Key, document.Kind, document.Path)], VersionStamp.Create(), document.Path)),
                document.Path,
                document.IsGenerated)).ToArray();
            var info = ProjectInfo.Create(
                id,
                VersionStamp.Create(),
                project.Name,
                project.AssemblyName,
                project.Language,
                filePath: project.Key.ProjectPath,
                outputFilePath: project.OutputFilePath,
                compilationOptions: SnapshotOptionFacts.ReconstructCompilation(project, workspace),
                parseOptions: SnapshotOptionFacts.ReconstructParse(project.ParseOptions),
                documents: DocumentsOfKind(project, documents, "source"),
                projectReferences: project.ProjectReferences.Select(reference => new ProjectReference(
                    ids[reference.Target], reference.Aliases.ToImmutableArray(), reference.EmbedInteropTypes)),
                metadataReferences: project.MetadataReferences.Select(reference => CreateReference(reference, metadata)),
                analyzerReferences: project.AnalyzerReferences.Select(reference =>
                    new AnalyzerFileReference(reference.OriginalPath, new NoExecutionAnalyzerLoader())),
                additionalDocuments: DocumentsOfKind(project, documents, "additional"))
                .WithAnalyzerConfigDocuments(DocumentsOfKind(project, documents, "analyzer-config"));
            solution = solution.AddProject(info)
                .WithProjectDefaultNamespace(id, project.DefaultNamespace)
                .WithProjectOutputRefFilePath(id, project.OutputRefFilePath)
                .WithProjectCompilationOutputInfo(id, new CompilationOutputInfo()
                    .WithAssemblyPath(project.CompilationAssemblyPath)
                    .WithGeneratedFilesOutputDirectory(project.GeneratedFilesOutputDirectory));
        }
        return solution;
    }

    private static IEnumerable<DocumentInfo> DocumentsOfKind(
        SnapshotProject project,
        IReadOnlyList<DocumentInfo> documents,
        string kind) => project.Documents.Select((document, index) => (document, index))
            .Where(item => item.document.Kind == kind)
            .Select(item => documents[item.index]);

    private static PortableExecutableReference CreateReference(
        SnapshotMetadataReference reference,
        IReadOnlyDictionary<(string Path, MetadataImageKind Kind), Metadata> metadata)
    {
        var kind = Enum.Parse<MetadataImageKind>(reference.Kind);
        var properties = new MetadataReferenceProperties(kind, reference.Aliases.ToImmutableArray(), reference.EmbedInteropTypes);
        // H-001 excludes external XML only. Image identity and every reference property remain mandatory.
        return new OwnedMetadataReference(metadata[(reference.Path, kind)], properties, reference.Path);
    }

    private static async Task<Dictionary<ProjectId, CSharpCompilation>> CreateBaseCompilationsAsync(
        Solution solution,
        WorkspaceSnapshot snapshot,
        IReadOnlyDictionary<SnapshotProjectKey, ProjectId> ids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProjectId, CSharpCompilation>();
        foreach (var id in solution.GetProjectDependencyGraph().GetTopologicallySortedProjects(cancellationToken))
        {
            var project = solution.GetProject(id)!;
            var trees = new List<SyntaxTree>();
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.SourceCodeKind != SourceCodeKind.Regular)
                {
                    throw new InvalidDataException("The base-query profile excludes script source.");
                }
                trees.Add(CSharpSyntaxTree.ParseText(
                    await document.GetTextAsync(cancellationToken),
                    (CSharpParseOptions)project.ParseOptions!,
                    document.FilePath!,
                    cancellationToken));
            }
            var references = project.MetadataReferences.ToList();
            foreach (var edge in project.ProjectReferences)
            {
                if (!result.TryGetValue(edge.ProjectId, out var dependency))
                {
                    throw new InvalidDataException("The base-query profile excludes cyclic project references.");
                }
                references.Add(dependency.ToMetadataReference(edge.Aliases, edge.EmbedInteropTypes));
            }
            // GetCompilationAsync could run generators. Build only the finite base compiler graph;
            // the fresh project supplies its own analyzer-config SyntaxTreeOptionsProvider.
            var compilation = CSharpCompilation.Create(
                project.AssemblyName, trees, references, (CSharpCompilationOptions)project.CompilationOptions!);
            var reason = SnapshotSigningProfile.UnsupportedReason(compilation);
            if (reason is not null)
            {
                var key = snapshot.Projects.Single(item => ids[item.Key] == id).Key;
                throw new InvalidDataException(
                    $"stage=hydrate project={project.Name} configuration={key.Configuration} tfm={key.TargetFramework} "
                    + $"operation=base-query reason={reason} route=refusal");
            }
            result.Add(id, compilation);
        }
        return result;
    }

    private static Dictionary<string, ImmutableArray<HydratedInputOccurrence>> RebindInputs(
        WorkspaceSnapshot snapshot,
        Solution solution,
        IReadOnlyDictionary<SnapshotProjectKey, ProjectId> ids)
    {
        var result = new Dictionary<string, ImmutableArray<HydratedInputOccurrence>>(InputPathCanon.Comparer);
        foreach (var input in snapshot.InputPaths)
        {
            var occurrences = ImmutableArray.CreateBuilder<HydratedInputOccurrence>();
            foreach (var occurrence in input.Occurrences)
            {
                var owner = ids[occurrence.Owner];
                var project = solution.GetProject(owner)!;
                var documents = occurrence.DocumentKind switch
                {
                    "source" => project.Documents.Cast<TextDocument>(),
                    "additional" => project.AdditionalDocuments,
                    "analyzer-config" => project.AnalyzerConfigDocuments,
                    _ => [],
                };
                var documentId = occurrence.DocumentKind is null ? null : documents.Single(document =>
                    InputPathCanon.Comparer.Equals(document.FilePath, input.Path)).Id;
                occurrences.Add(new HydratedInputOccurrence(
                    owner,
                    documentId,
                    occurrence.Role,
                    occurrence.RoleSource,
                    occurrence.Producer is null ? null : ids[occurrence.Producer],
                    occurrence.ProducerSource,
                    File.Exists(input.Path)));
            }
            result.Add(input.Path, occurrences.ToImmutable());
        }
        return result;
    }

    private Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken) =>
        _readBytes is null ? File.ReadAllBytesAsync(path, cancellationToken) : _readBytes(path, cancellationToken);

    private static SourceText DecodeText(byte[] bytes, SnapshotTextMetadata metadata)
    {
        var encoding = EncodingFor(metadata);
        using var stream = new MemoryStream(bytes, writable: false);
        var algorithm = Enum.Parse<SourceHashAlgorithm>(metadata.ChecksumAlgorithm);
        var text = SourceText.From(stream, encoding, algorithm, throwIfBinaryDetected: true);
        // An absent captured encoding stays absent; guessing it would silently grant a future writer
        // a persistence policy. Current characters and checksum are still rebuilt from current bytes.
        return encoding is null ? SourceText.From(text.ToString(), encoding: null, algorithm) : text;
    }

    private static Encoding? EncodingFor(SnapshotTextMetadata metadata)
    {
        if (metadata.EncodingCodePage is not { } codePage)
        {
            return null;
        }
        var bom = metadata.EncodingPreamble.Length > 0;
        Encoding encoding = codePage switch
        {
            65001 => new UTF8Encoding(bom, throwOnInvalidBytes: true),
            1200 => new UnicodeEncoding(bigEndian: false, bom, throwOnInvalidBytes: true),
            1201 => new UnicodeEncoding(bigEndian: true, bom, throwOnInvalidBytes: true),
            12000 => new UTF32Encoding(bigEndian: false, bom, throwOnInvalidCharacters: true),
            12001 => new UTF32Encoding(bigEndian: true, bom, throwOnInvalidCharacters: true),
            _ => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        };
        if (!encoding.GetPreamble().SequenceEqual(metadata.EncodingPreamble))
        {
            throw new InvalidDataException("The captured encoding preamble is not publicly representable.");
        }
        return encoding;
    }

    private sealed class OwnedMetadataReference : PortableExecutableReference
    {
        public OwnedMetadataReference(Metadata metadata, MetadataReferenceProperties properties, string path)
            : base(properties, path, DocumentationProvider.Default) => _metadata = metadata;

        protected override DocumentationProvider CreateDocumentationProvider() => DocumentationProvider.Default;

        protected override Metadata GetMetadataImpl() => _metadata;

        protected override PortableExecutableReference WithPropertiesImpl(MetadataReferenceProperties properties) =>
            new OwnedMetadataReference(_metadata, properties, FilePath!);

        private readonly Metadata _metadata;
    }

    private sealed class NoExecutionAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) =>
            throw new InvalidOperationException("The snapshot does not authorize analyzer or generator execution.");
    }

    private readonly Func<string, CancellationToken, Task<byte[]>>? _readBytes;
    private readonly Action<AdhocWorkspace>? _workspaceDisposed;
    private readonly Action<Metadata>? _metadataCreated;
}
