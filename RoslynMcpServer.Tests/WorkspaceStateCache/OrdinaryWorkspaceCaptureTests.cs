using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
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
    public async Task Real_plugin_load_captures_memberships_options_and_bound_roles()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        var commentPath = Path.Combine(root, "Services", "Workspace", "StateCache", "Models", "WorkspaceSnapshot.cs");
        var commentBefore = await File.ReadAllBytesAsync(commentPath);
        Assert.Contains("Portable graph facts only", File.ReadAllText(commentPath), StringComparison.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var result = await OrdinaryWorkspaceCapture.CaptureAsync(
            host.CurrentSolution, descriptors, path, properties, originalAnalyzerPathsProven: true, timeout.Token);
        Assert.True(result.UnsupportedReason is null, result.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(result.Snapshot);
        Assert.Equal(OrdinaryWorkspaceCapture.SchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(OrdinaryWorkspaceCapture.Profile, snapshot.Profile);
        Assert.Null(snapshot.Properties.RawRequested.Configuration);
        Assert.Null(snapshot.Properties.LoaderGlobals.Platform);
        Assert.Equal(2, snapshot.Projects.Length);
        var plugin = Assert.Single(snapshot.Projects, project => project.Name == "RoslynMcpPlugin");
        var server = Assert.Single(snapshot.Projects, project => project.Name == "RoslynMcpServer");
        Assert.Equal("Debug", plugin.Key.Configuration);
        Assert.Equal("net10.0", plugin.Key.TargetFramework);
        Assert.Equal(server.Key, Assert.Single(plugin.ProjectReferences).Target);
        Assert.Empty(server.ProjectReferences);
        AssertRealOptionsAndReferences(host.CurrentSolution, descriptors, snapshot);
        AssertOccurrenceBinding(host.CurrentSolution, path, snapshot);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("public sealed class SamplePlugin", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Portable graph facts only", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RoslynMcpServer.AnalyzerShadowCopy", json, StringComparison.Ordinal);
        foreach (var info in descriptors)
        {
            Assert.DoesNotContain(info.Id.Id.ToString(), json, StringComparison.Ordinal);
        }

        Assert.Equal(commentBefore, await File.ReadAllBytesAsync(commentPath));
        await WriteInventoryAsync(root, snapshot, timeout.Token);
    }

    [Fact]
    public async Task Live_descriptor_divergence_is_unsupported()
    {
        var path = Path.Combine(FindRoot(), "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var plugin = Assert.Single(descriptors, info => info.Name == "RoslynMcpPlugin");
        var project = host.CurrentSolution.GetProject(plugin.Id);
        Assert.NotNull(project);
        var parse = Assert.IsType<CSharpParseOptions>(project.ParseOptions);
        var changedSymbols = host.CurrentSolution.WithProjectParseOptions(
            plugin.Id,
            parse.WithPreprocessorSymbols(parse.PreprocessorSymbolNames.Concat(["CAPTURE_DIVERGENCE"])));
        var symbols = await CaptureAsync(changedSymbols, descriptors, path, properties, timeout.Token);
        Assert.Null(symbols.Snapshot);
        Assert.Equal("parse-options-descriptor-live-mismatch", symbols.UnsupportedReason);

        var document = Assert.Single(
            project.Documents,
            candidate => candidate.FilePath?.EndsWith("SamplePlugin.cs", StringComparison.OrdinalIgnoreCase) == true);
        var renamed = host.CurrentSolution.WithDocumentName(document.Id, document.Name + ".diverged");
        var name = await CaptureAsync(renamed, descriptors, path, properties, timeout.Token);
        Assert.Null(name.Snapshot);
        Assert.Equal("document-descriptor-live-mismatch", name.UnsupportedReason);

        var moved = host.CurrentSolution.WithDocumentFolders(document.Id, document.Folders.Append("Diverged"));
        var folders = await CaptureAsync(moved, descriptors, path, properties, timeout.Token);
        Assert.Null(folders.Snapshot);
        Assert.Equal("document-descriptor-live-mismatch", folders.UnsupportedReason);

        var otherKind = document.SourceCodeKind == SourceCodeKind.Regular
            ? SourceCodeKind.Script
            : SourceCodeKind.Regular;
        var rekinded = document.WithSourceCodeKind(otherKind).Project.Solution;
        var kind = await CaptureAsync(rekinded, descriptors, path, properties, timeout.Token);
        Assert.Null(kind.Snapshot);
        Assert.Equal("document-descriptor-live-mismatch", kind.UnsupportedReason);
    }

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

    private static void AssertRealOptionsAndReferences(
        Solution solution,
        IReadOnlyList<ProjectInfo> descriptors,
        WorkspaceSnapshot snapshot)
    {
        Assert.Equal(
            descriptors.Sum(info => info.Documents.Count),
            snapshot.Projects.Sum(project => project.Documents.Count(document => document.Kind == "source")));
        Assert.Equal(
            descriptors.Sum(info => info.AdditionalDocuments.Count),
            snapshot.Projects.Sum(project => project.Documents.Count(document => document.Kind == "additional")));
        Assert.Equal(
            descriptors.Sum(info => info.MetadataReferences.Count),
            snapshot.Projects.Sum(project => project.MetadataReferences.Length));
        Assert.Equal(
            descriptors.Sum(info => info.AnalyzerReferences.Count),
            snapshot.Projects.Sum(project => project.AnalyzerReferences.Length));
        foreach (var project in snapshot.Projects)
        {
            var live = Assert.Single(solution.Projects, candidate => candidate.Name == project.Name);
            var liveOptions = Assert.IsType<CSharpCompilationOptions>(live.CompilationOptions);
            var liveParse = Assert.IsType<CSharpParseOptions>(live.ParseOptions);
            Assert.Equal("Debug", project.EvaluatedProperties.Values.Configuration);
            Assert.Equal("net10.0", project.Key.TargetFramework);
            Assert.Empty(project.EvaluatedProperties.UnknownProperties);
            Assert.Equal(OrdinaryWorkspaceCapture.EvaluatedPropertySource, project.EvaluatedProperties.Source);
            Assert.Equal(liveParse.DocumentationMode.ToString(), project.ParseOptions.DocumentationMode);
            Assert.Equal(liveParse.SpecifiedLanguageVersion.ToString(), project.ParseOptions.SpecifiedLanguageVersion);
            Assert.Equal(liveParse.PreprocessorSymbolNames, project.ParseOptions.PreprocessorSymbols);
            Assert.Equal("csharp-command-line-debug-plus-v1", project.CompilationOptions.ConstructionWitness);
            Assert.Equal(SnapshotSigningProfile.IndependentPolicy, project.CompilationOptions.StrongNamePolicy);
            Assert.Equal(liveOptions.NullableContextOptions.ToString(), project.CompilationOptions.NullableContextOptions);
            Assert.Equal(liveOptions.AllowUnsafe, project.CompilationOptions.AllowUnsafe);
            Assert.Equal(liveOptions.PublicSign, project.CompilationOptions.PublicSign);
            Assert.Equal(liveOptions.CryptoKeyFile, project.CompilationOptions.CryptoKeyFile);
            Assert.NotEmpty(project.MetadataReferences);
            Assert.Contains(
                project.MetadataReferences,
                reference => reference.Path.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)
                    || reference.Path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            foreach (var reference in project.MetadataReferences)
            {
                var liveReference = Assert.Single(
                    live.MetadataReferences.OfType<PortableExecutableReference>(),
                    candidate => InputPathCanon.Comparer.Equals(InputPathCanon.TryCanonicalize(candidate.FilePath), reference.Path));
                Assert.Equal(liveReference.Properties.Kind.ToString(), reference.Kind);
                Assert.Equal(liveReference.Properties.Aliases, reference.Aliases);
                Assert.Equal(liveReference.Properties.EmbedInteropTypes, reference.EmbedInteropTypes);
                Assert.Equal(OrdinaryWorkspaceCapture.BinaryDocumentationPolicy, reference.BinaryDocumentationPolicy);
            }

            foreach (var reference in project.ProjectReferences)
            {
                var liveReference = Assert.Single(
                    live.ProjectReferences,
                    candidate => snapshot.Projects.Single(item => item.Key == reference.Target).Key
                        == KeyOf(solution, snapshot, candidate.ProjectId));
                Assert.Equal(liveReference.Aliases, reference.Aliases);
                Assert.Equal(liveReference.EmbedInteropTypes, reference.EmbedInteropTypes);
            }

            Assert.All(project.AnalyzerReferences, reference =>
            {
                Assert.Equal(OrdinaryWorkspaceCapture.AnalyzerIdentitySource, reference.IdentitySource);
                Assert.False(reference.OriginalPath.Contains(
                    "RoslynMcpServer.AnalyzerShadowCopy",
                    StringComparison.OrdinalIgnoreCase));
            });
            Assert.All(project.Documents, document => Assert.NotEmpty(document.TextMetadata.Checksum));
        }
    }

    private static void AssertOccurrenceBinding(Solution solution, string workspacePath, WorkspaceSnapshot snapshot)
    {
        var keys = snapshot.Projects.ToDictionary(
            project => Assert.Single(solution.Projects, candidate =>
                InputPathCanon.Comparer.Equals(InputPathCanon.TryCanonicalize(candidate.FilePath), project.Key.ProjectPath)).Id,
            project => project.Key);
        var map = WorkspaceInputMapBuilder.Build(solution, Guid.NewGuid(), Path.GetFullPath(workspacePath), loadGraphComplete: false);
        foreach (var project in snapshot.Projects)
        {
            var live = solution.Projects.Single(candidate => keys[candidate.Id] == project.Key);
            foreach (var document in project.Documents)
            {
                var liveDocument = live.Documents.Cast<TextDocument>()
                    .Concat(live.AdditionalDocuments)
                    .Concat(live.AnalyzerConfigDocuments)
                    .Single(candidate => InputPathCanon.Comparer.Equals(InputPathCanon.TryCanonicalize(candidate.FilePath), document.Path)
                        && KindOf(candidate, live) == document.Kind);
                var mapRole = Assert.Single(
                    map.Find(document.Path)!.Occurrences,
                    occurrence => occurrence.DocumentId == liveDocument.Id).Role;
                var captured = Assert.Single(
                    snapshot.InputPaths.Single(input => input.Path == document.Path).Occurrences,
                    occurrence => occurrence.Owner == project.Key && occurrence.DocumentKind == document.Kind);
                Assert.Equal(mapRole.ToString(), captured.Role);
                Assert.Equal(SnapshotOccurrenceBinder.DocumentRoleSource, captured.RoleSource);
                if (mapRole == InputRole.Generated)
                {
                    Assert.Equal(project.Key, captured.Producer);
                    Assert.Equal(SnapshotOccurrenceBinder.GeneratedProducerSource, captured.ProducerSource);
                }
                else
                {
                    Assert.Null(captured.Producer);
                    Assert.Null(captured.ProducerSource);
                }
            }
        }

        var evaluationPaths = snapshot.InputPaths
            .SelectMany(input => input.Occurrences
                .Where(occurrence => occurrence.Role == "EvaluationInput")
                .Select(occurrence => (input.Path, occurrence.Owner)))
            .ToArray();
        Assert.Equal(snapshot.Projects.Length, evaluationPaths.Length);
        Assert.All(evaluationPaths, pair => Assert.Contains(
            snapshot.Projects,
            project => project.Key == pair.Owner && InputPathCanon.Comparer.Equals(project.Key.ProjectPath, pair.Path)));
        Assert.Contains(snapshot.InputPaths, input => input.Occurrences.Any(occurrence => occurrence.Role == "Unknown"));
        Assert.Contains(
            snapshot.InputPaths,
            input => input.Occurrences.Count(occurrence => occurrence.DocumentKind == "analyzer-config") > 1);
        var props = Path.GetFullPath(Path.Combine(FindRoot(), "Directory.Build.props"));
        Assert.DoesNotContain(snapshot.InputPaths, input => InputPathCanon.Comparer.Equals(input.Path, props));
    }

    private static string KindOf(TextDocument document, Project project)
    {
        if (project.Documents.Any(candidate => candidate.Id == document.Id))
        {
            return "source";
        }

        if (project.AdditionalDocuments.Any(candidate => candidate.Id == document.Id))
        {
            return "additional";
        }

        return "analyzer-config";
    }

    private static SnapshotProjectKey KeyOf(Solution solution, WorkspaceSnapshot snapshot, ProjectId projectId)
    {
        var path = InputPathCanon.TryCanonicalize(solution.GetProject(projectId)!.FilePath);
        return Assert.Single(snapshot.Projects, project => InputPathCanon.Comparer.Equals(project.Key.ProjectPath, path)).Key;
    }

    private static async Task WriteInventoryAsync(string root, WorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        var roles = snapshot.InputPaths
            .SelectMany(input => input.Occurrences)
            .GroupBy(occurrence => occurrence.Role)
            .ToDictionary(group => group.Key, group => group.Count());
        var inventory = new
        {
            snapshot.SchemaVersion,
            snapshot.Profile,
            snapshot.RoslynVersion,
            Projects = snapshot.Projects.Select(project => new
            {
                project.Name,
                project.Key.Configuration,
                project.Key.Platform,
                project.Key.TargetFramework,
                Source = project.Documents.Count(document => document.Kind == "source"),
                Additional = project.Documents.Count(document => document.Kind == "additional"),
                AnalyzerConfig = project.Documents.Count(document => document.Kind == "analyzer-config"),
                Metadata = project.MetadataReferences.Length,
                Analyzers = project.AnalyzerReferences.Length,
                ProjectReferences = project.ProjectReferences.Length,
            }),
            Roles = roles,
            Sha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))),
        };
        var directory = Path.Combine(root, "TestResults", "task-03");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "capture-observation.json"),
            json,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "capture-inventory.json"),
            JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
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

    private static Task<SnapshotCaptureResult> CaptureAsync(
        Solution solution,
        IReadOnlyList<ProjectInfo> descriptors,
        string path,
        SnapshotLoadProperties properties,
        CancellationToken cancellationToken) =>
        OrdinaryWorkspaceCapture.CaptureAsync(
            solution,
            descriptors,
            path,
            properties,
            originalAnalyzerPathsProven: true,
            cancellationToken);

    private static string Canonical(string path) => InputPathCanon.TryCanonicalize(path)!;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
