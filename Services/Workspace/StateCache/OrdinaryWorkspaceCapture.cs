using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Analyzers;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Builds a schema-v1 snapshot from an ordinary loaded solution and its pre-overlay project descriptors.
/// The caller owns the load. This type does not open a workspace and does not change
/// <see cref="SolutionManager"/>.
/// The input-map generation is process-local and is not serialized. Walk-up, import, and restore
/// paths stay out of the snapshot: a path under an intermediate directory is not a proven manifest.
/// </summary>
internal static class OrdinaryWorkspaceCapture
{
    public const int SchemaVersion = 1;

    public const string Profile = "s001-single-target-debug-base-query-h001-h002-v1";

    public const string AnalyzerIdentitySource = "ordinary-pre-overlay-public-loader-descriptor";

    public const string BinaryDocumentationPolicy = "H-001-external-xml-excluded";

    public const string EvaluatedPropertySource =
        "public-msbuild-evaluation-with-same-globals-and-compilation-attribute-witness";

    public static async Task<SnapshotCaptureResult> CaptureAsync(
        Solution solution,
        IReadOnlyList<ProjectInfo> descriptors,
        string workspacePath,
        SnapshotLoadProperties properties,
        bool originalAnalyzerPathsProven,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(properties);

        try
        {
            var snapshot = await CaptureSnapshotAsync(
                solution,
                descriptors,
                workspacePath,
                properties,
                originalAnalyzerPathsProven,
                cancellationToken).ConfigureAwait(false);
            return new SnapshotCaptureResult(snapshot, null);
        }
        catch (InvalidOperationException exception)
        {
            return new SnapshotCaptureResult(null, exception.Message);
        }
    }

    internal static void RejectShadowAnalyzerPath(string workspacePath, string analyzerPath)
    {
        var canonical = Canonical(analyzerPath);
        Require(!HasPathSegment(canonical, "RoslynMcpServer.AnalyzerShadowCopy"), "analyzer-identity-from-shadow-path");
        var shadowRoot = Canonical(AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(workspacePath));
        Require(!IsUnderDirectory(canonical, shadowRoot), "analyzer-identity-from-shadow-path");
    }

    private static async Task<WorkspaceSnapshot> CaptureSnapshotAsync(
        Solution solution,
        IReadOnlyList<ProjectInfo> descriptors,
        string workspacePath,
        SnapshotLoadProperties properties,
        bool originalAnalyzerPathsProven,
        CancellationToken cancellationToken)
    {
        var canonicalWorkspace = Canonical(workspacePath);
        Require(originalAnalyzerPathsProven, "original-analyzer-paths-unproven");
        Require(solution.ProjectIds.Count > 0, "empty-project-inventory");
        RequireUniqueProjects(solution.Projects.Select(project => project.FilePath));
        var keys = await BindInstancesAsync(solution, properties.LoaderGlobals, cancellationToken).ConfigureAwait(false);
        Require(descriptors.Count == keys.Count, "descriptor-project-inventory-mismatch");
        var projects = new List<SnapshotProject>(descriptors.Count);
        var bindings = new List<SnapshotDocumentBinding>();
        foreach (var info in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = solution.GetProject(info.Id);
            Require(project is not null, "descriptor-project-binding-unavailable");
            Require(project!.FilePath == info.FilePath, "descriptor-project-path-mismatch");
            Require(keys.ContainsKey(info.Id), "descriptor-project-binding-unavailable");
            Require(info.ParseOptions is CSharpParseOptions, "non-csharp-parse-options");
            Require(
                project.ProjectReferences.All(reference => keys.ContainsKey(reference.ProjectId)),
                "directed-project-binding-unavailable");
            var documents = await CaptureDocumentsAsync(project, info, cancellationToken).ConfigureAwait(false);
            bindings.AddRange(documents.Bindings);
            projects.Add(CreateProject(info, project, keys, documents.Documents, canonicalWorkspace));
        }

        // loadGraphComplete stays false: this map is the loaded public graph, not a dependency closure.
        var map = WorkspaceInputMapBuilder.Build(solution, Guid.NewGuid(), canonicalWorkspace, loadGraphComplete: false);
        var inputPaths = SnapshotOccurrenceBinder.Bind(map, projects, bindings, keys);
        var version = typeof(CSharpCompilation).Assembly.GetName().Version;
        Require(version is not null, "roslyn-version-unavailable");
        return new WorkspaceSnapshot(
            SchemaVersion,
            Profile,
            version!.ToString(),
            canonicalWorkspace,
            properties,
            projects.ToArray(),
            inputPaths);
    }

    private static SnapshotProject CreateProject(
        ProjectInfo info,
        Project project,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys,
        SnapshotDocument[] documents,
        string workspacePath)
    {
        var key = keys[info.Id];
        return new SnapshotProject(
            key,
            new SnapshotEvaluatedProperties(
                new SnapshotPropertySet(key.Configuration, key.Platform, key.TargetFramework),
                EvaluatedPropertySource,
                []),
            info.Name,
            info.AssemblyName,
            info.Language,
            CanonicalOptional(project.OutputFilePath),
            CanonicalOptional(project.OutputRefFilePath),
            project.DefaultNamespace,
            CanonicalOptional(project.CompilationOutputInfo.AssemblyPath),
            CanonicalOptional(project.CompilationOutputInfo.GeneratedFilesOutputDirectory),
            ParseOptions(project, info),
            SnapshotOptionFacts.Compilation(project, info.CompilationOptions, SnapshotSigningProfile.IndependentPolicy),
            documents,
            project.ProjectReferences.Select(reference =>
            {
                Require(keys.TryGetValue(reference.ProjectId, out var target), "directed-project-binding-unavailable");
                return new SnapshotProjectReference(target!, Aliases(reference.Aliases), reference.EmbedInteropTypes);
            }).ToArray(),
            CaptureMetadata(project),
            CaptureAnalyzers(project, info, workspacePath));
    }

    private static async Task<Dictionary<ProjectId, SnapshotProjectKey>> BindInstancesAsync(
        Solution solution,
        SnapshotPropertySet globals,
        CancellationToken cancellationToken)
    {
        // ProjectCollection.LoadProject has no async surface. One collection evaluates every instance
        // with the caller's globals; absent globals stay absent and are not replaced with saved defaults.
        using var evaluation = new Microsoft.Build.Evaluation.ProjectCollection(
            MsBuildWorkspaceProperties.Create(globals.Configuration, globals.Platform, globals.TargetFramework));
        var keys = new Dictionary<ProjectId, SnapshotProjectKey>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = evaluation.LoadProject(project.FilePath!);
            var configuration = evaluated.GetPropertyValue("Configuration");
            var platform = evaluated.GetPropertyValue("Platform");
            var framework = evaluated.GetPropertyValue("TargetFramework");
            Require(
                !string.IsNullOrEmpty(configuration)
                && !string.IsNullOrEmpty(platform)
                && !string.IsNullOrEmpty(framework)
                && string.IsNullOrEmpty(evaluated.GetPropertyValue("TargetFrameworks")),
                "evaluated-instance-unavailable-or-multitarget");
            Require(configuration == "Debug", "outside-debug-option-witness-profile");
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) as CSharpCompilation;
            Require(compilation is not null, "compilation-state-unavailable");
            Require(
                Attribute(compilation!, "System.Reflection.AssemblyConfigurationAttribute") == configuration
                && Attribute(compilation!, "System.Runtime.Versioning.TargetFrameworkAttribute")
                    == evaluated.GetPropertyValue("TargetFrameworkMoniker"),
                "evaluated-instance-binding-unproven");
            var signing = SnapshotSigningProfile.UnsupportedReason(compilation!);
            Require(signing is null, signing ?? "signing-state-unknown");
            Require(
                !(await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false)).Any(),
                "generator-dependent-scope-unsupported");
            keys.Add(project.Id, new SnapshotProjectKey(Canonical(project.FilePath!), configuration, platform, framework));
        }

        return keys;
    }

    private static string? Attribute(CSharpCompilation compilation, string typeName)
    {
        var values = compilation.Assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == typeName)
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static async Task<(SnapshotDocument[] Documents, SnapshotDocumentBinding[] Bindings)> CaptureDocumentsAsync(
        Project project,
        ProjectInfo info,
        CancellationToken cancellationToken)
    {
        var descriptors = info.Documents.Select(document => (Info: document, Kind: "source"))
            .Concat(info.AdditionalDocuments.Select(document => (Info: document, Kind: "additional")))
            .Concat(info.AnalyzerConfigDocuments.Select(document => (Info: document, Kind: "analyzer-config")))
            .ToArray();
        var actual = project.Documents.Cast<TextDocument>()
            .Concat(project.AdditionalDocuments)
            .Concat(project.AnalyzerConfigDocuments)
            .ToDictionary(document => document.Id);
        Require(descriptors.Length == actual.Count, "document-inventory-mismatch");
        var memberships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<SnapshotDocument>(descriptors.Length);
        var bindings = new List<SnapshotDocumentBinding>(descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = descriptor.Info;
            Require(
                document.FilePath is not null && actual.TryGetValue(document.Id, out _),
                "document-path-or-binding-unavailable");
            var path = Canonical(document.FilePath!);
            Require(memberships.Add(descriptor.Kind + "\n" + path), "ambiguous-document-membership");
            var live = actual[document.Id];
            RequireLiveDocumentIdentity(live, document);
            var text = await live.GetTextAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            var disk = SourceText.From(stream, text.Encoding, text.ChecksumAlgorithm);
            Require(
                disk.ContentEquals(text) && disk.GetChecksum().SequenceEqual(text.GetChecksum()),
                "document-disk-state-mismatch");
            var captured = new SnapshotDocument(
                descriptor.Kind,
                path,
                live.Name,
                live.Folders.ToArray(),
                SourceCodeKindOf(live, document),
                document.IsGenerated,
                new SnapshotTextMetadata(
                    text.Encoding?.CodePage,
                    text.Encoding?.GetPreamble() ?? [],
                    text.ChecksumAlgorithm.ToString(),
                    text.GetChecksum().ToArray()));
            documents.Add(captured);
            bindings.Add(new SnapshotDocumentBinding(
                project.Id,
                document.Id,
                captured,
                RoleSource(live, document.IsGenerated)));
        }

        return (documents.ToArray(), bindings.ToArray());
    }

    private static SnapshotParseOptions ParseOptions(Project project, ProjectInfo info)
    {
        // The descriptor and the live project are one ordinary load. A later edit of either
        // side is a different compilation: keeping the older symbols would restore semantics
        // the workspace no longer has. This scope refuses the mismatch instead of choosing.
        Require(project.ParseOptions is CSharpParseOptions, "non-csharp-parse-options");
        Require(
            ((CSharpParseOptions)project.ParseOptions!).Equals(info.ParseOptions),
            "parse-options-descriptor-live-mismatch");
        return SnapshotOptionFacts.Parse((CSharpParseOptions)info.ParseOptions!);
    }

    private static void RequireLiveDocumentIdentity(TextDocument live, DocumentInfo descriptor)
    {
        Require(live.Name == descriptor.Name, "document-descriptor-live-mismatch");
        Require(live.Folders.SequenceEqual(descriptor.Folders), "document-descriptor-live-mismatch");
        if (live is Document source)
        {
            Require(source.SourceCodeKind == descriptor.SourceCodeKind, "document-descriptor-live-mismatch");
        }
    }

    private static string SourceCodeKindOf(TextDocument live, DocumentInfo descriptor) =>
        live is Document source ? source.SourceCodeKind.ToString() : descriptor.SourceCodeKind.ToString();

    private static string RoleSource(TextDocument live, bool descriptorGenerated)
    {
        var liveGenerated = RoslynDocumentBuildSideEffect.TryGetIsGenerated(live);
        if (liveGenerated is null)
        {
            Require(!descriptorGenerated, "document-generated-flag-unproven");
            return SnapshotOccurrenceBinder.UnavailableGeneratedFlagSource;
        }

        Require(liveGenerated.Value == descriptorGenerated, "document-generated-flag-unproven");
        return SnapshotOccurrenceBinder.DocumentRoleSource;
    }

    private static SnapshotMetadataReference[] CaptureMetadata(Project project) =>
        project.MetadataReferences.Select(reference =>
        {
            // H-001 leaves binary documentation text out of the snapshot. The reference, its aliases,
            // and EmbedInteropTypes stay. Source comments remain on disk with the parse options.
            Require(reference is PortableExecutableReference { FilePath: not null }, "non-file-metadata-unsupported");
            var file = (PortableExecutableReference)reference;
            return new SnapshotMetadataReference(
                Canonical(file.FilePath!),
                file.Properties.Kind.ToString(),
                Aliases(file.Properties.Aliases),
                file.Properties.EmbedInteropTypes,
                BinaryDocumentationPolicy);
        }).ToArray();

    private static SnapshotAnalyzerReference[] CaptureAnalyzers(Project project, ProjectInfo info, string workspacePath)
    {
        Require(
            project.AnalyzerReferences.All(reference => reference.FullPath is not null)
            && info.AnalyzerReferences.All(reference => reference is AnalyzerFileReference && reference.FullPath is not null),
            "non-file-analyzer-unsupported");
        var loaded = project.AnalyzerReferences.Select(reference => Canonical(reference.FullPath!)).ToArray();
        var described = info.AnalyzerReferences.Select(reference => Canonical(reference.FullPath!)).ToArray();
        Require(loaded.SequenceEqual(described, InputPathCanon.Comparer), "analyzer-descriptor-inventory-mismatch");
        return described.Select(path =>
        {
            RejectShadowAnalyzerPath(workspacePath, path);
            return new SnapshotAnalyzerReference(path, AnalyzerIdentitySource);
        }).ToArray();
    }

    private static string[] Aliases(ImmutableArray<string> aliases) =>
        aliases.IsDefault ? [] : aliases.ToArray();

    private static string? CanonicalOptional(string? path) => path is null ? null : Canonical(path);

    private static void RequireUniqueProjects(IEnumerable<string?> paths)
    {
        var inventory = paths.ToArray();
        Require(inventory.All(path => path is not null), "project-path-unavailable");
        Require(
            inventory.Select(path => Canonical(path!)).Distinct(InputPathCanon.Comparer).Count() == inventory.Length,
            "ambiguous-project-instance");
    }

    private static string Canonical(string path) =>
        InputPathCanon.TryCanonicalize(path) ?? throw new InvalidOperationException("path-not-canonicalizable");

    private static bool HasPathSegment(string path, string segment)
    {
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (InputPathCanon.Comparer.Equals(path, directory))
        {
            return true;
        }

        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, InputPathCanon.Comparison);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
