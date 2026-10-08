using System.Text.Json;
using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>
/// Same-process schema consumer for ordinary, pre-overlay public descriptors. This does not
/// bind an input generation, implement reusable capture, or authorize analyzer execution.
/// </summary>
internal static class RealControlSchemaCapture
{
    public const int SchemaVersion = 1;
    public const string Profile = "s001-single-target-debug-base-query-h001-h002-v1";

    public static async Task<SnapshotCaptureResult> CaptureAsync(
        Solution solution,
        IReadOnlyList<ProjectInfo> descriptors,
        string workspacePath,
        SnapshotLoadProperties properties,
        bool originalAnalyzerPathsProven,
        CancellationToken cancellationToken)
    {
        try
        {
            Require(originalAnalyzerPathsProven, "original-analyzer-paths-unproven");
            Require(solution.ProjectIds.Count > 0, "empty-project-inventory");
            var keys = await CreateKeysAsync(solution, properties.LoaderGlobals, cancellationToken);
            Require(descriptors.Count == keys.Count, "descriptor-project-inventory-mismatch");
            var projects = new List<SnapshotProject>();
            foreach (var info in descriptors)
            {
                var project = solution.GetProject(info.Id);
                Require(project is not null, "descriptor-project-binding-unavailable");
                Require(project!.FilePath == info.FilePath, "descriptor-project-path-mismatch");
                Require(info.ParseOptions is CSharpParseOptions, "non-csharp-parse-options");
                Require(project.ProjectReferences.All(reference => keys.ContainsKey(reference.ProjectId)),
                    "directed-project-binding-unavailable");
                var documents = await CaptureDocumentsAsync(project, info, cancellationToken);
                projects.Add(new SnapshotProject(keys[info.Id],
                    new SnapshotEvaluatedProperties(new SnapshotPropertySet(keys[info.Id].Configuration,
                        keys[info.Id].Platform, keys[info.Id].TargetFramework),
                        "public-msbuild-evaluation-with-same-globals-and-compilation-attribute-witness", []),
                    info.Name, info.AssemblyName, info.Language, info.OutputFilePath, project.OutputRefFilePath,
                    project.DefaultNamespace, project.CompilationOutputInfo.AssemblyPath,
                    project.CompilationOutputInfo.GeneratedFilesOutputDirectory,
                    SnapshotOptionsCapture.Parse((CSharpParseOptions)info.ParseOptions!),
                    SnapshotOptionsCapture.Compilation(project, info.CompilationOptions), documents,
                    project.ProjectReferences.Select(reference => new SnapshotProjectReference(
                        keys[reference.ProjectId], reference.Aliases.ToArray(), reference.EmbedInteropTypes)).ToArray(),
                    CaptureMetadata(project), CaptureAnalyzers(project, info)));
            }
            var snapshot = new WorkspaceSnapshot(SchemaVersion, Profile,
                typeof(CSharpCompilation).Assembly.GetName().Version!.ToString(), Path.GetFullPath(workspacePath),
                properties, projects.ToArray(), CaptureInputPaths(projects));
            return new SnapshotCaptureResult(snapshot, null);
        }
        catch (InvalidOperationException exception)
        {
            return new SnapshotCaptureResult(null, exception.Message);
        }
    }

    public static void RequireUniqueProjects(IEnumerable<string?> paths)
    {
        var inventory = paths.ToArray();
        Require(inventory.All(path => path is not null), "project-path-unavailable");
        Require(inventory.Select(path => Canonical(path!)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == inventory.Length, "ambiguous-project-instance");
    }

    public static SnapshotInputPath[] CaptureInputPaths(IReadOnlyList<SnapshotProject> projects)
    {
        var paths = new Dictionary<string, List<SnapshotInputOccurrence>>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            Add(project.Key.ProjectPath, project, null, "EvaluationInput", "loaded-project-path", null, null);
            foreach (var output in new[] { project.OutputFilePath, project.OutputRefFilePath }.Where(path => path is not null).Distinct())
            {
                Add(output!, project, null, "Output", "public-project-output", project.Key, "public-project-output");
            }
            foreach (var document in project.Documents)
            {
                var role = DocumentRole(document);
                Add(document.Path, project, document.Kind, role, "public-document-descriptor-and-conservative-input-map-policy",
                    document.IsGenerated ? project.Key : null, document.IsGenerated ? "public-document-generated-flag" : null);
            }
            foreach (var reference in project.MetadataReferences)
            {
                var producers = projects.Where(candidate => new[] { candidate.OutputFilePath, candidate.OutputRefFilePath }
                    .Any(output => output is not null && StringComparer.OrdinalIgnoreCase.Equals(Canonical(output), reference.Path)))
                    .Select(candidate => candidate.Key).Distinct().ToArray();
                var producer = producers.Length == 1 && project.ProjectReferences.Any(edge => edge.Target == producers[0])
                    ? producers[0] : null;
                Add(reference.Path, project, null, "MetadataDependency", "public-metadata-reference", producer,
                    producer is not null ? "unique-output-and-directed-project-binding" : null);
            }
            foreach (var analyzer in project.AnalyzerReferences)
            {
                Add(analyzer.OriginalPath, project, null, "AnalyzerDependency", analyzer.IdentitySource, null, null);
            }
        }
        return paths.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new SnapshotInputPath(pair.Key, pair.Value.ToArray())).ToArray();

        void Add(string path, SnapshotProject project, string? documentKind, string role, string source,
            SnapshotProjectKey? producer, string? producerSource)
        {
            path = Canonical(path);
            if (!paths.TryGetValue(path, out var occurrences))
            {
                occurrences = [];
                paths.Add(path, occurrences);
            }
            occurrences.Add(new SnapshotInputOccurrence(project.Key, documentKind, role, source,
                producer, producerSource, File.Exists(path)));
        }
    }

    private static async Task<Dictionary<ProjectId, SnapshotProjectKey>> CreateKeysAsync(
        Solution solution, SnapshotPropertySet globals, CancellationToken cancellationToken)
    {
        RequireUniqueProjects(solution.Projects.Select(project => project.FilePath));
        using var evaluation = new ProjectCollection(MsBuildWorkspaceProperties.Create(
            globals.Configuration, globals.Platform, globals.TargetFramework));
        var keys = new Dictionary<ProjectId, SnapshotProjectKey>();
        foreach (var project in solution.Projects)
        {
            var evaluated = evaluation.LoadProject(project.FilePath!);
            var configuration = evaluated.GetPropertyValue("Configuration");
            var platform = evaluated.GetPropertyValue("Platform");
            var framework = evaluated.GetPropertyValue("TargetFramework");
            Require(!string.IsNullOrEmpty(configuration) && !string.IsNullOrEmpty(platform)
                && !string.IsNullOrEmpty(framework) && string.IsNullOrEmpty(evaluated.GetPropertyValue("TargetFrameworks")),
                "evaluated-instance-unavailable-or-multitarget");
            Require(configuration == "Debug", "outside-debug-option-witness-profile");
            var compilation = await project.GetCompilationAsync(cancellationToken) as CSharpCompilation;
            Require(compilation is not null, "compilation-state-unavailable");
            Require(Attribute(compilation!, "System.Reflection.AssemblyConfigurationAttribute") == configuration
                && Attribute(compilation!, "System.Runtime.Versioning.TargetFrameworkAttribute")
                    == evaluated.GetPropertyValue("TargetFrameworkMoniker"), "evaluated-instance-binding-unproven");
            var outcome = CapabilityOperationGate.Execute(compilation!, "selected-symbols", true, () => { }, null, _ => { });
            Require(outcome == "supported-query", outcome);
            Require(!(await project.GetSourceGeneratedDocumentsAsync(cancellationToken)).Any(),
                "generator-dependent-scope-unsupported");
            keys.Add(project.Id, new SnapshotProjectKey(Canonical(project.FilePath!), configuration, platform, framework));
        }
        return keys;
    }

    private static string? Attribute(CSharpCompilation compilation, string typeName)
    {
        var values = compilation.Assembly.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString()
            == typeName).Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static async Task<SnapshotDocument[]> CaptureDocumentsAsync(
        Microsoft.CodeAnalysis.Project project, ProjectInfo info, CancellationToken cancellationToken)
    {
        var descriptors = info.Documents.Select(document => (Info: document, Kind: "source"))
            .Concat(info.AdditionalDocuments.Select(document => (Info: document, Kind: "additional")))
            .Concat(info.AnalyzerConfigDocuments.Select(document => (Info: document, Kind: "analyzer-config"))).ToArray();
        var actual = project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments)
            .Concat(project.AnalyzerConfigDocuments).ToDictionary(document => document.Id);
        Require(descriptors.Length == actual.Count, "document-inventory-mismatch");
        var memberships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<SnapshotDocument>();
        foreach (var descriptor in descriptors)
        {
            var document = descriptor.Info;
            Require(document.FilePath is not null && actual.TryGetValue(document.Id, out _), "document-path-or-binding-unavailable");
            var path = Canonical(document.FilePath!);
            Require(memberships.Add(JsonSerializer.Serialize(new[] { descriptor.Kind, path })), "ambiguous-document-membership");
            var text = await actual[document.Id].GetTextAsync(cancellationToken);
            using var stream = File.OpenRead(path);
            var disk = Microsoft.CodeAnalysis.Text.SourceText.From(stream, text.Encoding, text.ChecksumAlgorithm);
            Require(disk.ContentEquals(text) && disk.GetChecksum().SequenceEqual(text.GetChecksum()), "document-disk-state-mismatch");
            documents.Add(new SnapshotDocument(descriptor.Kind, path, document.Name, document.Folders.ToArray(),
                document.SourceCodeKind.ToString(), document.IsGenerated,
                new SnapshotTextMetadata(text.Encoding?.CodePage, text.Encoding?.GetPreamble() ?? [],
                    text.ChecksumAlgorithm.ToString(), text.GetChecksum().ToArray())));
        }
        return documents.ToArray();
    }

    private static SnapshotMetadataReference[] CaptureMetadata(Microsoft.CodeAnalysis.Project project) =>
        project.MetadataReferences.Select(reference =>
        {
            Require(reference is PortableExecutableReference { FilePath: not null }, "non-file-metadata-unsupported");
            var file = (PortableExecutableReference)reference;
            return new SnapshotMetadataReference(Canonical(file.FilePath!), file.Properties.Kind.ToString(),
                file.Properties.Aliases.ToArray(), file.Properties.EmbedInteropTypes, "H-001-external-xml-excluded");
        }).ToArray();

    private static SnapshotAnalyzerReference[] CaptureAnalyzers(Microsoft.CodeAnalysis.Project project, ProjectInfo info)
    {
        var paths = project.AnalyzerReferences.Select(reference => reference.FullPath).ToArray();
        Require(paths.SequenceEqual(info.AnalyzerReferences.Select(reference => reference.FullPath)),
            "analyzer-descriptor-inventory-mismatch");
        return info.AnalyzerReferences.Select(reference =>
        {
            Require(reference is AnalyzerFileReference && reference.FullPath is not null, "non-file-analyzer-unsupported");
            return new SnapshotAnalyzerReference(Canonical(reference.FullPath!), "ordinary-pre-overlay-public-loader-descriptor");
        }).ToArray();
    }

    private static string DocumentRole(SnapshotDocument document)
    {
        if (document.IsGenerated)
        {
            return "Generated";
        }
        var name = Path.GetFileName(document.Path);
        if (document.Kind == "source" && (name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase)))
        {
            return "Unknown";
        }
        return document.Kind switch
        {
            "source" => "UserInput",
            "additional" => "AdditionalFile",
            "analyzer-config" => "AnalyzerConfig",
            _ => "Unknown",
        };
    }

    private static string Canonical(string path) => InputPathCanon.TryCanonicalize(path)
        ?? throw new InvalidOperationException("path-not-canonicalizable");

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
