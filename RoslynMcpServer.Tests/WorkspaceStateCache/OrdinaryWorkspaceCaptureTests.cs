using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Analyzers;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class OrdinaryWorkspaceCaptureTests
{
    public OrdinaryWorkspaceCaptureTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Missing_analyzer_path_witness_is_unsupported()
    {
        using var host = new AdhocWorkspace();
        var result = await OrdinaryWorkspaceCapture.CaptureAsync(
            host.CurrentSolution,
            [],
            "control.csproj",
            new(new(null, null, null), new(null, null, null)),
            originalAnalyzerPathsProven: false,
            CancellationToken.None);
        Assert.Null(result.Snapshot);
        Assert.Equal("original-analyzer-paths-unproven", result.UnsupportedReason);
    }

    [Fact]
    public void Shadow_analyzer_path_is_unsupported()
    {
        var workspace = Path.GetFullPath("control.csproj");
        var shadow = Path.Combine(
            AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(workspace),
            "Foo.dll");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OrdinaryWorkspaceCapture.RejectShadowAnalyzerPath(workspace, shadow));
        Assert.Equal("analyzer-identity-from-shadow-path", exception.Message);
    }

    [Fact]
    public async Task Duplicate_project_path_is_unsupported()
    {
        using var host = new AdhocWorkspace();
        var path = Path.GetFullPath("control.csproj");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        host.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "A", "A", LanguageNames.CSharp, path, compilationOptions: options));
        host.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "B", "B", LanguageNames.CSharp, path, compilationOptions: options));
        var result = await OrdinaryWorkspaceCapture.CaptureAsync(
            host.CurrentSolution,
            [],
            path,
            new(new(null, null, null), new(null, null, null)),
            originalAnalyzerPathsProven: true,
            CancellationToken.None);
        Assert.Null(result.Snapshot);
        Assert.Equal("ambiguous-project-instance", result.UnsupportedReason);
    }

    [Theory]
    [InlineData("public-sign")]
    [InlineData("key-file")]
    [InlineData("key-attribute")]
    [InlineData("signed-friend")]
    public void Signing_dependent_state_is_unsupported(string kind)
    {
        var compilation = kind switch
        {
            "public-sign" => CSharpCompilation.Create(
                "Signed",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithPublicSign(true)),
            "key-file" => CSharpCompilation.Create(
                "Signed",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithCryptoKeyFile("company.snk")),
            "key-attribute" => AttributeCompilation("System.Reflection.AssemblyKeyFile(\"company.snk\")"),
            "signed-friend" => AttributeCompilation(SignedFriendAttribute()),
            _ => throw new InvalidOperationException(kind),
        };
        Assert.Equal("signing-dependent", SnapshotSigningProfile.UnsupportedReason(compilation));
    }

    [Fact]
    public void Unresolved_signing_attribute_is_unsupported()
    {
        var compilation = AttributeCompilation("MissingSigningAttribute");
        Assert.Equal("signing-state-unknown", SnapshotSigningProfile.UnsupportedReason(compilation));
    }

    [Fact]
    public void Unsigned_friend_assembly_remains_supported()
    {
        var compilation = AttributeCompilation("System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")");
        Assert.Null(SnapshotSigningProfile.UnsupportedReason(compilation));
    }

    [Fact]
    public void Obj_directory_and_source_suffix_do_not_prove_a_producer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ordinary-capture-" + Guid.NewGuid().ToString("N"));
        var projectId = ProjectId.CreateNewId();
        var projectPath = Path.Combine(root, "Sample.csproj");
        var assemblyInfo = Path.Combine(root, "obj", "Debug", "net10.0", "Sample.AssemblyInfo.cs");
        var plain = Path.Combine(root, "obj", "Debug", "net10.0", "Plain.cs");
        var generated = Path.Combine(root, "obj", "Debug", "net10.0", "FromFlag.cs");
        using var workspace = new AdhocWorkspace();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            filePath: projectPath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            documents:
            [
                Document(projectId, "Sample.AssemblyInfo.cs", assemblyInfo, isGenerated: false),
                Document(projectId, "Plain.cs", plain, isGenerated: false),
                Document(projectId, "FromFlag.cs", generated, isGenerated: true),
            ]));
        var paths = BindSolution(workspace.CurrentSolution);
        var owner = Assert.Single(paths.SelectMany(input => input.Occurrences).Select(occurrence => occurrence.Owner).Distinct());
        AssertRole(paths, assemblyInfo, owner, "Unknown");
        AssertRole(paths, plain, owner, "UserInput");
        var flagged = AssertRole(paths, generated, owner, "Generated");
        Assert.Equal(owner, flagged.Producer);
        Assert.Equal(SnapshotOccurrenceBinder.GeneratedProducerSource, flagged.ProducerSource);
        Assert.All(
            paths.SelectMany(input => input.Occurrences).Where(occurrence => occurrence.Role != "Generated"),
            occurrence => Assert.Null(occurrence.Producer));
    }

    [Fact]
    public void Metadata_producer_follows_the_directed_edge()
    {
        var binary = typeof(object).Assembly.Location;
        var withEdge = CaptureMetadataBinding(binary, projectReference: true, sharedOutput: false);
        Assert.Equal(withEdge.ProducerKey, withEdge.Metadata.Producer);
        Assert.Equal(SnapshotOccurrenceBinder.MetadataProducerSource, withEdge.Metadata.ProducerSource);

        var missingEdge = CaptureMetadataBinding(binary, projectReference: false, sharedOutput: false);
        Assert.Null(missingEdge.Metadata.Producer);
        Assert.Null(missingEdge.Metadata.ProducerSource);

        var ambiguous = CaptureMetadataBinding(binary, projectReference: true, sharedOutput: true);
        Assert.Null(ambiguous.Metadata.Producer);
    }

    private static SnapshotInputPath[] BindSolution(Solution solution)
    {
        var keys = KeysOf(solution);
        var projects = solution.Projects.Select(project => SnapshotOf(project, keys)).ToArray();
        var bindings = new List<SnapshotDocumentBinding>();
        foreach (var project in solution.Projects)
        {
            bindings.AddRange(Bindings(project, project.Documents, "source"));
            bindings.AddRange(Bindings(project, project.AdditionalDocuments, "additional"));
            bindings.AddRange(Bindings(project, project.AnalyzerConfigDocuments, "analyzer-config"));
        }

        var map = WorkspaceInputMapBuilder.Build(solution, Guid.NewGuid(), loadedWorkspacePath: null, loadGraphComplete: false);
        return SnapshotOccurrenceBinder.Bind(map, projects, bindings, keys);
    }

    private static Dictionary<ProjectId, SnapshotProjectKey> KeysOf(Solution solution) =>
        solution.Projects.ToDictionary(
            project => project.Id,
            project => new SnapshotProjectKey(
                InputPathCanon.TryCanonicalize(project.FilePath)!,
                "Debug",
                "AnyCPU",
                "net10.0"));

    private static IEnumerable<SnapshotDocumentBinding> Bindings(
        Project project,
        IEnumerable<TextDocument> documents,
        string kind)
    {
        foreach (var document in documents)
        {
            var generated = RoslynDocumentBuildSideEffect.TryGetIsGenerated(document);
            yield return new SnapshotDocumentBinding(
                project.Id,
                document.Id,
                new SnapshotDocument(
                    kind,
                    InputPathCanon.TryCanonicalize(document.FilePath)!,
                    document.Name,
                    document.Folders.ToArray(),
                    document is Document source ? source.SourceCodeKind.ToString() : nameof(SourceCodeKind.Regular),
                    generated == true,
                    new SnapshotTextMetadata(null, [], "Sha256", [])),
                generated is null
                    ? SnapshotOccurrenceBinder.UnavailableGeneratedFlagSource
                    : SnapshotOccurrenceBinder.DocumentRoleSource);
        }
    }

    private static SnapshotProject SnapshotOf(Project project, IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys)
    {
        var key = keys[project.Id];
        return new SnapshotProject(
            key,
            new SnapshotEvaluatedProperties(new SnapshotPropertySet(key.Configuration, key.Platform, key.TargetFramework), "test", []),
            project.Name,
            project.AssemblyName,
            project.Language,
            InputPathCanon.TryCanonicalize(project.OutputFilePath),
            InputPathCanon.TryCanonicalize(project.OutputRefFilePath),
            project.DefaultNamespace,
            null,
            null,
            new SnapshotParseOptions("Default", "Default", "Parse", "Regular", [], []),
            UnusedOptions(),
            [],
            project.ProjectReferences.Select(reference => new SnapshotProjectReference(
                keys[reference.ProjectId],
                reference.Aliases.ToArray(),
                reference.EmbedInteropTypes)).ToArray(),
            project.MetadataReferences.OfType<PortableExecutableReference>().Select(reference => new SnapshotMetadataReference(
                InputPathCanon.TryCanonicalize(reference.FilePath)!,
                reference.Properties.Kind.ToString(),
                reference.Properties.Aliases.ToArray(),
                reference.Properties.EmbedInteropTypes,
                OrdinaryWorkspaceCapture.BinaryDocumentationPolicy)).ToArray(),
            []);
    }

    private static (SnapshotProjectKey ProducerKey, SnapshotInputOccurrence Metadata) CaptureMetadataBinding(
        string binary,
        bool projectReference,
        bool sharedOutput)
    {
        var root = Path.Combine(Path.GetTempPath(), "ordinary-capture-" + Guid.NewGuid().ToString("N"));
        var producerId = ProjectId.CreateNewId();
        var consumerId = ProjectId.CreateNewId();
        var producerPath = Path.Combine(root, "Producer.csproj");
        var consumerPath = Path.Combine(root, "Consumer.csproj");
        var consumerOutput = sharedOutput ? binary : Path.Combine(root, "consumer.dll");
        var reference = new ProjectReference(producerId, ImmutableArray.Create("EdgeAlias"), embedInteropTypes: true);
        var metadataReference = MetadataReference.CreateFromFile(
            binary,
            new MetadataReferenceProperties(aliases: ImmutableArray.Create("Lib"), embedInteropTypes: true));
        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(ProjectInfo.Create(
                producerId,
                VersionStamp.Create(),
                "Producer",
                "Producer",
                LanguageNames.CSharp,
                filePath: producerPath,
                outputFilePath: binary,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddProject(ProjectInfo.Create(
                consumerId,
                VersionStamp.Create(),
                "Consumer",
                "Consumer",
                LanguageNames.CSharp,
                filePath: consumerPath,
                outputFilePath: consumerOutput,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [metadataReference],
                projectReferences: projectReference ? [reference] : []));
        var capturedMetadata = Assert.Single(SnapshotOf(solution.GetProject(consumerId)!, KeysOf(solution)).MetadataReferences);
        Assert.Equal(["Lib"], capturedMetadata.Aliases);
        Assert.True(capturedMetadata.EmbedInteropTypes);
        if (projectReference)
        {
            var capturedEdge = Assert.Single(SnapshotOf(solution.GetProject(consumerId)!, KeysOf(solution)).ProjectReferences);
            Assert.Equal(["EdgeAlias"], capturedEdge.Aliases);
            Assert.True(capturedEdge.EmbedInteropTypes);
        }

        var paths = BindSolution(solution);
        var producerKey = Assert.Single(
            paths.Where(input => InputPathCanon.Comparer.Equals(input.Path, Canonical(producerPath)))
                .SelectMany(input => input.Occurrences),
            occurrence => occurrence.Role == "EvaluationInput").Owner;
        var metadata = Assert.Single(
            paths.Single(input => InputPathCanon.Comparer.Equals(input.Path, Canonical(binary))).Occurrences,
            occurrence => occurrence.Role == "MetadataDependency");
        return (producerKey, metadata);
    }

    private static SnapshotInputOccurrence AssertRole(
        IReadOnlyList<SnapshotInputPath> paths,
        string path,
        SnapshotProjectKey owner,
        string role)
    {
        var occurrence = Assert.Single(
            paths.Single(input => InputPathCanon.Comparer.Equals(input.Path, Canonical(path))).Occurrences,
            candidate => candidate.Owner == owner);
        Assert.Equal(role, occurrence.Role);
        if (role == "Generated")
        {
            return occurrence;
        }

        Assert.Null(occurrence.Producer);
        Assert.Null(occurrence.ProducerSource);
        return occurrence;
    }

    private static DocumentInfo Document(ProjectId projectId, string name, string path, bool isGenerated) =>
        DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name,
            filePath: path,
            isGenerated: isGenerated);

    private static CSharpCompilation AttributeCompilation(string attribute) =>
        CSharpCompilation.Create(
            "AttributeControl",
            [CSharpSyntaxTree.ParseText("[assembly: " + attribute + "]")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static string SignedFriendAttribute()
    {
        var publicKey = Convert.ToHexString(typeof(object).Assembly.GetName().GetPublicKey()!);
        return "System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend, PublicKey=" + publicKey + "\")";
    }

    private static SnapshotCompilationOptions UnusedOptions() => new(
        "csharp-command-line-debug-plus-v1",
        "DynamicallyLinkedLibrary",
        null,
        null,
        null,
        [],
        "Debug",
        false,
        false,
        null,
        null,
        [],
        null,
        false,
        "AnyCpu",
        "Default",
        4,
        [],
        true,
        true,
        "Public",
        "Disable",
        false,
        "desktop-default",
        new SnapshotResolver("none", null, [], []),
        new SnapshotResolver("none", null, [], []),
        new SnapshotResolver("command-line-project-factory-v1", AppContext.BaseDirectory, [], []),
        SnapshotSigningProfile.IndependentPolicy,
        "none");

    private static string Canonical(string path) => InputPathCanon.TryCanonicalize(path)!;
}
