using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Tests.WorkspaceStateCache.Models;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>
/// Bounded compiler reconstruction, not a workspace snapshot or an admission policy.
/// Analyzer identities remain in the observed project inventory; no analyzer is executed here.
/// Document membership and IsGenerated come from public loader descriptors; producer provenance is not inferred.
/// </summary>
internal static class RealControlReconstruction
{
    public static async Task<ReconstructedControl> CreateAsync(
        Solution ordinary,
        IReadOnlyList<ProjectInfo> descriptors,
        AdhocWorkspace factoryWorkspace,
        CancellationToken cancellationToken)
    {
        var (restoredSolution, restoredDescriptors) = await RestoreSolutionAsync(
            ordinary, descriptors, factoryWorkspace, cancellationToken);
        var result = new Dictionary<ProjectId, CSharpCompilation>();
        foreach (var id in ordinary.GetProjectDependencyGraph().GetTopologicallySortedProjects(cancellationToken))
        {
            var project = ordinary.GetProject(id)!;
            var restoredProject = restoredSolution.Projects.Single(candidate => candidate.FilePath == project.FilePath);
            foreach (var originalDocument in project.Documents.Cast<TextDocument>()
                .Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments))
            {
                var restoredDocument = restoredProject.Documents.Cast<TextDocument>()
                    .Concat(restoredProject.AdditionalDocuments).Concat(restoredProject.AnalyzerConfigDocuments)
                    .Single(candidate => candidate.FilePath == originalDocument.FilePath);
                var expected = await originalDocument.GetTextAsync(cancellationToken);
                var actual = await restoredDocument.GetTextAsync(cancellationToken);
                Require(actual.ContentEquals(expected), "Reconstructed document text differs.");
                Require(actual.Encoding?.CodePage == expected.Encoding?.CodePage
                    && (actual.Encoding?.GetPreamble() ?? []).SequenceEqual(expected.Encoding?.GetPreamble() ?? []),
                    "Reconstructed document encoding differs.");
                Require(actual.ChecksumAlgorithm == expected.ChecksumAlgorithm
                    && actual.GetChecksum().SequenceEqual(expected.GetChecksum()), "Reconstructed document checksum differs.");
            }
            var parse = (CSharpParseOptions)restoredProject.ParseOptions!;
            var trees = new List<SyntaxTree>();
            foreach (var document in project.Documents)
            {
                var heldText = await document.GetTextAsync(cancellationToken);
                using var stream = File.OpenRead(document.FilePath!);
                var diskText = SourceText.From(stream, heldText.Encoding, heldText.ChecksumAlgorithm);
                Require(diskText.ContentEquals(heldText), "Source bytes differ from the ordinary document.");
                var restoredDocument = restoredProject.Documents.Single(candidate => candidate.FilePath == document.FilePath);
                Require((await restoredDocument.GetTextAsync(cancellationToken)).ContentEquals(heldText),
                    "The reconstructed document loader changed source text.");
                Require(document.SourceCodeKind == SourceCodeKind.Regular, "Only regular source is admitted.");
                trees.Add(CSharpSyntaxTree.ParseText(diskText, parse, document.FilePath!, cancellationToken));
            }

            var references = new List<MetadataReference>();
            foreach (var reference in project.MetadataReferences)
            {
                Require(reference is PortableExecutableReference { FilePath: not null }, "Non-file metadata is unsupported.");
                var file = (PortableExecutableReference)reference;
                // H-001 explicitly excludes binary XML documentation. All reference properties survive.
                references.Add(MetadataReference.CreateFromFile(file.FilePath!, file.Properties, DocumentationProvider.Default));
            }
            foreach (var reference in project.ProjectReferences)
            {
                references.Add(result[reference.ProjectId].ToMetadataReference(reference.Aliases, reference.EmbedInteropTypes));
            }
            result.Add(id, CSharpCompilation.Create(restoredProject.AssemblyName, trees, references,
                (CSharpCompilationOptions)restoredProject.CompilationOptions!));
        }
        return new ReconstructedControl(restoredSolution, result, restoredDescriptors);
    }

    private static async Task<(Solution Solution, IReadOnlyList<ProjectInfo> Descriptors)> RestoreSolutionAsync(
        Solution ordinary, IReadOnlyList<ProjectInfo> descriptors, AdhocWorkspace target,
        CancellationToken cancellationToken)
    {
        var ids = descriptors.ToDictionary(info => info.Id, _ => ProjectId.CreateNewId());
        var solution = target.CurrentSolution;
        var restoredDescriptors = new List<ProjectInfo>();
        foreach (var info in descriptors)
        {
            var project = ordinary.GetProject(info.Id)!;
            var id = ids[info.Id];
            var options = RecreateOptions(project, target);
            var metadata = info.MetadataReferences.Cast<PortableExecutableReference>()
                .Select(reference => MetadataReference.CreateFromFile(reference.FilePath!, reference.Properties,
                    DocumentationProvider.Default));
            var analyzers = info.AnalyzerReferences.Select(reference =>
                new AnalyzerFileReference(reference.FullPath!, new NoExecutionAnalyzerLoader()));
            var parse = (CSharpParseOptions)info.ParseOptions!;
            var restoredParse = new CSharpParseOptions(parse.SpecifiedLanguageVersion, parse.DocumentationMode,
                parse.Kind, parse.PreprocessorSymbolNames).WithFeatures(parse.Features);
            Require(restoredParse.Equals(parse), "Parse options were not reconstructed exactly.");
            var restoredInfo = ProjectInfo.Create(id, VersionStamp.Create(), info.Name,
                info.AssemblyName, info.Language, filePath: info.FilePath, outputFilePath: info.OutputFilePath,
                compilationOptions: options, parseOptions: restoredParse,
                documents: await Task.WhenAll(info.Documents.Select(document =>
                    RecreateDocumentAsync(document, project, id, cancellationToken))),
                projectReferences: info.ProjectReferences.Select(reference =>
                    new ProjectReference(ids[reference.ProjectId], reference.Aliases, reference.EmbedInteropTypes)),
                metadataReferences: metadata, analyzerReferences: analyzers,
                additionalDocuments: await Task.WhenAll(info.AdditionalDocuments.Select(document =>
                    RecreateDocumentAsync(document, project, id, cancellationToken))))
                .WithAnalyzerConfigDocuments(await Task.WhenAll(info.AnalyzerConfigDocuments.Select(document =>
                    RecreateDocumentAsync(document, project, id, cancellationToken))));
            restoredDescriptors.Add(restoredInfo);
            solution = solution.AddProject(restoredInfo)
                .WithProjectDefaultNamespace(id, project.DefaultNamespace)
                .WithProjectOutputRefFilePath(id, project.OutputRefFilePath)
                .WithProjectCompilationOutputInfo(id, project.CompilationOutputInfo);
        }
        return (solution, restoredDescriptors);
    }

    private static async Task<DocumentInfo> RecreateDocumentAsync(
        DocumentInfo info, Project original, ProjectId id, CancellationToken cancellationToken)
    {
        var document = original.Documents.Cast<TextDocument>().Concat(original.AdditionalDocuments)
            .Concat(original.AnalyzerConfigDocuments).Single(candidate => candidate.Id == info.Id);
        var text = await document.GetTextAsync(cancellationToken);
        return DocumentInfo.Create(DocumentId.CreateNewId(id), info.Name, info.Folders, info.SourceCodeKind,
            new CapturedFileTextLoader(info.FilePath!, text.Encoding, text.ChecksumAlgorithm), info.FilePath, info.IsGenerated);
    }

    private static CSharpCompilationOptions RecreateOptions(Project project, Microsoft.CodeAnalysis.Workspace factoryWorkspace)
    {
        var original = (CSharpCompilationOptions)project.CompilationOptions!;
        var factory = CommandLineProject.CreateProjectInfo(project.Name, LanguageNames.CSharp,
            Array.Empty<string>(), Path.GetDirectoryName(project.FilePath!)!, factoryWorkspace).CompilationOptions!;
        var ordinaryFactory = CommandLineProject.CreateProjectInfo(project.Name, LanguageNames.CSharp,
            Array.Empty<string>(), Path.GetDirectoryName(project.FilePath!)!, project.Solution.Workspace).CompilationOptions!;
        // Equality includes the metadata service identity. Validate constructor inputs within the
        // original host, then construct a different resolver using the new host's service.
        Require(original.MetadataReferenceResolver is not null
            && original.MetadataReferenceResolver.Equals(ordinaryFactory.MetadataReferenceResolver),
            "The public factory did not reproduce the metadata resolver.");
        var source = original.SourceReferenceResolver as SourceFileResolver;
        Require(original.SourceReferenceResolver is null || source is not null, "Unknown source resolver.");
        var xml = original.XmlReferenceResolver as XmlFileResolver;
        Require(original.XmlReferenceResolver is null || xml is not null, "Unknown XML resolver.");
        Require(ReferenceEquals(original.AssemblyIdentityComparer, DesktopAssemblyIdentityComparer.Default)
            || ReferenceEquals(original.AssemblyIdentityComparer, AssemblyIdentityComparer.Default), "Unknown identity comparer.");

        // The ordinary Debug profile includes parser-owned state absent from public property getters.
        // /debug+ reconstructs it, and the final public Equals check refuses any different profile.
        var restored = CSharpCommandLineParser.Default.Parse(["/debug+"],
                Path.GetDirectoryName(project.FilePath!)!, sdkDirectory: null).CompilationOptions
            .WithOutputKind(original.OutputKind)
            .WithModuleName(original.ModuleName)
            .WithMainTypeName(original.MainTypeName)
            .WithScriptClassName(original.ScriptClassName)
            .WithUsings(original.Usings)
            .WithOptimizationLevel(original.OptimizationLevel)
            .WithOverflowChecks(original.CheckOverflow)
            .WithAllowUnsafe(original.AllowUnsafe)
            .WithCryptoKeyContainer(original.CryptoKeyContainer)
            .WithCryptoKeyFile(original.CryptoKeyFile)
            .WithCryptoPublicKey(original.CryptoPublicKey)
            .WithDelaySign(original.DelaySign)
            .WithPublicSign(original.PublicSign)
            .WithPlatform(original.Platform)
            .WithGeneralDiagnosticOption(original.GeneralDiagnosticOption)
            .WithWarningLevel(original.WarningLevel)
            .WithSpecificDiagnosticOptions(original.SpecificDiagnosticOptions)
            .WithConcurrentBuild(original.ConcurrentBuild)
            .WithDeterministic(original.Deterministic)
            .WithMetadataImportOptions(original.MetadataImportOptions)
            .WithNullableContextOptions(original.NullableContextOptions)
            .WithReportSuppressedDiagnostics(original.ReportSuppressedDiagnostics)
            .WithMetadataReferenceResolver(factory.MetadataReferenceResolver)
            .WithXmlReferenceResolver(xml is null ? null : new XmlFileResolver(xml.BaseDirectory))
            .WithSourceReferenceResolver(source is null ? null
                : new SourceFileResolver(source.SearchPaths, source.BaseDirectory, source.PathMap))
            .WithStrongNameProvider(new DesktopStrongNameProvider(ImmutableArray<string>.Empty))
            .WithAssemblyIdentityComparer(original.AssemblyIdentityComparer);
        // H-002 permits this declared empty-search-path policy only after the operation gate.
        // Every other compilation option must compare equal using Roslyn's public value contract.
        var comparable = restored.WithStrongNameProvider(original.StrongNameProvider)
            .WithMetadataReferenceResolver(original.MetadataReferenceResolver)
            .WithSyntaxTreeOptionsProvider(original.SyntaxTreeOptionsProvider);
        Require(comparable.Equals(original),
            "A mandatory compilation option changed during reconstruction.");
        return restored;
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }

    private sealed class CapturedFileTextLoader : TextLoader
    {
        public CapturedFileTextLoader(string path, Encoding? encoding, SourceHashAlgorithm checksumAlgorithm)
        {
            _path = path;
            _encoding = encoding;
            _checksumAlgorithm = checksumAlgorithm;
        }

        public override Task<TextAndVersion> LoadTextAndVersionAsync(
            LoadTextOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(_path);
            // A new host's default checksum algorithm can differ from ordinary MSBuild load.
            // Keep captured public text metadata, while reading source bytes from disk afresh.
            var text = SourceText.From(stream, _encoding, _checksumAlgorithm);
            return Task.FromResult(TextAndVersion.Create(text, VersionStamp.Create(), _path));
        }

        private readonly string _path;
        private readonly Encoding? _encoding;
        private readonly SourceHashAlgorithm _checksumAlgorithm;
    }

    private sealed class NoExecutionAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath) =>
            throw new InvalidOperationException("Analyzer/generator execution is excluded from this capability slice.");
    }
}
