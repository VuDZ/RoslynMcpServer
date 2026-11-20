using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

// Keep the real project loads in one xUnit class because design-time builds share obj files.
// https://github.com/VuDZ/RoslynMcpServer/actions/runs/44708161993/job/136193075040
public sealed class SharedCheckoutWorkspaceTests
{
    public SharedCheckoutWorkspaceTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Inspect_and_compare_real_control_without_claiming_workspace_readiness()
    {
        var root = RepositoryRoot.Find();
        var controlPaths = new[]
        {
            Path.Combine(root, "RoslynMcpServer.csproj"),
            Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj"),
            Path.Combine(root, "samples", "RoslynMcpPlugin", "SamplePlugin.cs"),
            Path.Combine(root, "samples", "RoslynMcpPlugin", "SampleTools.cs"),
        };
        var controlHashes = controlPaths.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var workspace = MSBuildWorkspace.Create();
        var plugin = await workspace.OpenProjectAsync(
            Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj"),
            cancellationToken: timeout.Token);
        Assert.DoesNotContain(workspace.Diagnostics, item => item.Kind == WorkspaceDiagnosticKind.Failure);
        var observations = new List<object>();
        foreach (var project in workspace.CurrentSolution.Projects)
        {
            var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
            var compilation = await project.GetCompilationAsync(timeout.Token);
            Assert.NotNull(compilation);
            observations.Add(new
            {
                project.Name,
                project.FilePath,
                project.OutputFilePath,
                project.AssemblyName,
                Documents = project.Documents.Count(),
                AdditionalDocuments = project.AdditionalDocuments.Count(),
                AnalyzerConfigs = project.AnalyzerConfigDocuments.Count(),
                Metadata = project.MetadataReferences.Select(reference => new
                {
                    Type = reference.GetType().FullName,
                    Path = (reference as PortableExecutableReference)?.FilePath,
                    reference.Properties,
                }),
                ProjectReferences = project.ProjectReferences.Select(reference => new
                {
                    Target = project.Solution.GetProject(reference.ProjectId)?.FilePath,
                    reference.Aliases,
                    reference.EmbedInteropTypes,
                }),
                Analyzers = project.AnalyzerReferences.Select(reference => new
                {
                    Type = reference.GetType().FullName,
                    reference.FullPath,
                }),
                StrongName = options.StrongNameProvider?.GetType().FullName,
                options.CryptoKeyFile,
                options.CryptoKeyContainer,
                PublicKeyLength = options.CryptoPublicKey.Length,
                options.PublicSign,
                options.DelaySign,
                XmlResolver = options.XmlReferenceResolver?.GetType().FullName,
                SourceResolver = options.SourceReferenceResolver?.GetType().FullName,
                MetadataResolver = options.MetadataReferenceResolver?.GetType().FullName,
                IdentityComparer = options.AssemblyIdentityComparer.GetType().FullName,
                AssemblyIdentity = compilation.Assembly.Identity.ToString(),
                Attributes = compilation.Assembly.GetAttributes().Select(attribute => attribute.ToString()),
                Generated = (await project.GetSourceGeneratedDocumentsAsync(timeout.Token))
                    .Select(document => new { document.Name, document.FilePath }).ToArray(),
                Errors = compilation.GetDiagnostics(timeout.Token)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()).ToArray(),
            });
        }
        var directory = Path.Combine(root, "TestResults", "task-08");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "inspection.json"),
            JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
        Assert.Equal("RoslynMcpPlugin", plugin.Name);
        using var captureHost = new AdhocWorkspace();
        var loader = new MSBuildProjectLoader(captureHost);
        var descriptors = await loader.LoadProjectInfoAsync(plugin.FilePath!, cancellationToken: timeout.Token);
        captureHost.AddProjects(descriptors);
        await AssertOrdinaryCaptureParityAsync(workspace.CurrentSolution, captureHost.CurrentSolution, timeout.Token);
        await CompareSelectedSemanticsAsync(captureHost.CurrentSolution, descriptors[0].Id, descriptors,
            directory, timeout.Token);
        foreach (var (path, hash) in controlHashes)
        {
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path, timeout.Token)));
        }
    }

    [Fact]
    public async Task Real_plugin_load_captures_memberships_options_and_bound_roles()
    {
        var root = RepositoryRoot.Find();
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
        var path = Path.Combine(RepositoryRoot.Find(), "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
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
    public async Task Real_ordinary_descriptors_fill_portable_schema_without_source_text_or_host_ids()
    {
        var root = RepositoryRoot.Find();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var result = await RealControlSchemaCapture.CaptureAsync(host.CurrentSolution, descriptors, path,
            properties, originalAnalyzerPathsProven: true, timeout.Token);
        Assert.True(result.UnsupportedReason is null, result.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(result.Snapshot);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(2, snapshot.Projects.Length);
        Assert.Null(snapshot.Properties.RawRequested.Configuration);
        Assert.Null(snapshot.Properties.LoaderGlobals.Configuration);
        Assert.All(snapshot.Projects, project =>
        {
            Assert.Equal("Debug", project.EvaluatedProperties.Values.Configuration);
            Assert.Equal("net10.0", project.Key.TargetFramework);
            Assert.Empty(project.EvaluatedProperties.UnknownProperties);
            Assert.NotEmpty(project.MetadataReferences);
            Assert.NotEmpty(project.AnalyzerReferences);
            Assert.All(project.MetadataReferences, reference => Assert.Equal("H-001-external-xml-excluded", reference.BinaryDocumentationPolicy));
            Assert.All(project.AnalyzerReferences, reference => Assert.Equal("ordinary-pre-overlay-public-loader-descriptor", reference.IdentitySource));
        });
        var plugin = snapshot.Projects.Single(project => project.Name == "RoslynMcpPlugin");
        var server = snapshot.Projects.Single(project => project.Name == "RoslynMcpServer");
        Assert.Equal(server.Key, Assert.Single(plugin.ProjectReferences).Target);
        Assert.Empty(server.ProjectReferences);
        Assert.Equal(descriptors.Sum(info => info.Documents.Count), snapshot.Projects.Sum(project => project.Documents.Count(document => document.Kind == "source")));
        Assert.Equal(descriptors.Sum(info => info.MetadataReferences.Count), snapshot.Projects.Sum(project => project.MetadataReferences.Length));
        Assert.Contains(snapshot.InputPaths, input => input.Occurrences.Any(occurrence => occurrence.Role == "Unknown"));
        foreach (var project in snapshot.Projects)
        {
            foreach (var document in project.Documents)
            {
                Assert.NotEmpty(document.TextMetadata.Checksum);
                var occurrence = Assert.Single(snapshot.InputPaths.Single(input => input.Path == document.Path).Occurrences,
                    occurrence => occurrence.Owner == project.Key && occurrence.DocumentKind == document.Kind);
                Assert.Equal(document.IsGenerated, occurrence.Role == "Generated");
            }
        }
        Assert.Contains(snapshot.InputPaths, input => input.Occurrences.Count(occurrence => occurrence.DocumentKind == "analyzer-config") > 1);
        CheckLinkedRolesAndProducerAmbiguity(snapshot, root);
        var json = JsonSerializer.Serialize(snapshot);
        foreach (var info in descriptors)
        {
            Assert.DoesNotContain(info.Id.Id.ToString(), json);
            Assert.All(info.Documents, document => Assert.DoesNotContain(document.Id.Id.ToString(), json));
        }
        Assert.DoesNotContain("public sealed class SamplePlugin", json);
        var directory = Path.Combine(root, "TestResults", "task-02");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "schema-observation.json"),
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
    }

    [Fact]
    public async Task Captured_control_round_trips()
    {
        MsBuildBootstrapper.Register();
        var root = RepositoryRoot.Find();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var result = await OrdinaryWorkspaceCapture.CaptureAsync(
            host.CurrentSolution,
            descriptors,
            path,
            properties,
            originalAnalyzerPathsProven: true,
            timeout.Token);
        Assert.True(result.UnsupportedReason is null, result.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(result.Snapshot);
        var encoded = WorkspaceSnapshotCodec.Encode(snapshot);
        var decoded = WorkspaceSnapshotCodec.Decode(encoded);
        Assert.Equal(encoded, WorkspaceSnapshotCodec.Encode(decoded));
        Assert.Equal(snapshot.Projects.Length, decoded.Projects.Length);
        Assert.Equal(snapshot.InputPaths.Length, decoded.InputPaths.Length);
    }

    [Fact]
    public async Task Real_ordinary_capture_codec_and_hydrate_preserve_the_selected_base_query()
    {
        var root = RepositoryRoot.Find();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var ordinary = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(ordinary).LoadProjectInfoAsync(
            path, cancellationToken: timeout.Token);
        ordinary.AddProjects(descriptors);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(
            ordinary.CurrentSolution,
            descriptors,
            path,
            new SnapshotLoadProperties(new(null, null, null), new(null, null, null)),
            originalAnalyzerPathsProven: true,
            timeout.Token);
        Assert.Null(capture.UnsupportedReason);
        var snapshot = WorkspaceSnapshotCodec.Decode(WorkspaceSnapshotCodec.Encode(capture.Snapshot!));
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, timeout.Token);
        Assert.Empty(session.Workspace.CurrentSolution.ProjectIds);
        Assert.DoesNotContain(session.Solution.ProjectIds, id => ordinary.CurrentSolution.ProjectIds.Contains(id));
        var pluginId = session.Projects.Single(pair => pair.Key.ProjectPath == path).Value;
        var plugin = session.BaseCompilations[pluginId];
        var sample = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin"));
        Assert.Contains(sample.AllInterfaces, symbol =>
            symbol.ToDisplayString() == "RoslynMcpServer.Plugins.IRoslynMcpPlugin"
            && symbol.ContainingAssembly.Name == "RoslynMcpServer");
        Assert.NotNull(plugin.GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken"));
        Assert.DoesNotContain(plugin.GetDiagnostics(timeout.Token), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var serverId = session.Projects.Single(pair => pair.Key.ProjectPath == Path.Combine(root, "RoslynMcpServer.csproj")).Value;
        var sourceComment = session.BaseCompilations[serverId]
            .GetTypeByMetadataName("RoslynMcpServer.Services.Workspace.StateCache.Models.WorkspaceSnapshot")!
            .GetDocumentationCommentXml(cancellationToken: timeout.Token);
        Assert.Contains("Portable graph facts only", sourceComment, StringComparison.Ordinal);
        foreach (var project in snapshot.Projects)
        {
            var live = ordinary.CurrentSolution.Projects.Single(item => item.FilePath == project.Key.ProjectPath);
            var restored = session.Solution.GetProject(session.Projects[project.Key])!;
            var options = Assert.IsType<CSharpCompilationOptions>(restored.CompilationOptions);
            var originalOptions = Assert.IsType<CSharpCompilationOptions>(live.CompilationOptions);
            Assert.True(options.WithStrongNameProvider(originalOptions.StrongNameProvider)
                .WithMetadataReferenceResolver(originalOptions.MetadataReferenceResolver)
                .WithSyntaxTreeOptionsProvider(originalOptions.SyntaxTreeOptionsProvider).Equals(originalOptions));
            Assert.NotSame(originalOptions.MetadataReferenceResolver, options.MetadataReferenceResolver);
            Assert.NotSame(originalOptions.SyntaxTreeOptionsProvider, options.SyntaxTreeOptionsProvider);
            Assert.Equal(live.ParseOptions, restored.ParseOptions);
            Assert.Equal(live.MetadataReferences.Count(), restored.MetadataReferences.Count());
            Assert.Equal(live.AnalyzerReferences.Count, restored.AnalyzerReferences.Count);
            foreach (var document in live.Documents.Cast<TextDocument>()
                .Concat(live.AdditionalDocuments).Concat(live.AnalyzerConfigDocuments))
            {
                var actualDocument = restored.Documents.Cast<TextDocument>()
                    .Concat(restored.AdditionalDocuments).Concat(restored.AnalyzerConfigDocuments)
                    .Single(item => item.FilePath == document.FilePath);
                Assert.NotEqual(document.Id, actualDocument.Id);
                AssertText(await document.GetTextAsync(timeout.Token), await actualDocument.GetTextAsync(timeout.Token));
            }
        }
    }

    [Fact]
    public async Task Real_control_roundtrip_in_a_new_pid()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var handoff = Environment.GetEnvironmentVariable(RoundtripProcess.DirectoryVariable);
        if (handoff is not null)
        {
            await RestoreAndCheckAsync(handoff, timeout.Token);
            return;
        }
        var root = RepositoryRoot.Find();
        var directory = Path.Combine(root, "TestResults", "task-06", "real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        await RoundtripObservation.CaptureAsync(path, directory, timeout.Token);
        await RoundtripProcess.RunAsync(
            directory,
            typeof(SharedCheckoutWorkspaceTests),
            nameof(Real_control_roundtrip_in_a_new_pid),
            timeout.Token);
    }

    private static async Task AssertOrdinaryCaptureParityAsync(
        Solution baseline, Solution capture, CancellationToken cancellationToken)
    {
        Assert.Equal(baseline.Projects.Select(project => project.FilePath).Order(),
            capture.Projects.Select(project => project.FilePath).Order());
        foreach (var project in baseline.Projects)
        {
            var captured = Assert.Single(capture.Projects, item => item.FilePath == project.FilePath);
            Assert.Equal(project.Documents.Select(item => item.FilePath), captured.Documents.Select(item => item.FilePath));
            Assert.Equal(project.MetadataReferences.Select(item => (item.Display, item.Properties)),
                captured.MetadataReferences.Select(item => (item.Display, item.Properties)));
            Assert.Equal(project.ProjectReferences.Select(item => baseline.GetProject(item.ProjectId)!.FilePath),
                captured.ProjectReferences.Select(item => capture.GetProject(item.ProjectId)!.FilePath));
            var expected = (await project.GetCompilationAsync(cancellationToken))!;
            var actual = (await captured.GetCompilationAsync(cancellationToken))!;
            Assert.Equal(expected.GetDiagnostics(cancellationToken).Select(item => item.ToString()).Order(),
                actual.GetDiagnostics(cancellationToken).Select(item => item.ToString()).Order());
        }
    }

    private static async Task CompareSelectedSemanticsAsync(
        Solution solution, ProjectId pluginId, IReadOnlyList<ProjectInfo> descriptors,
        string directory, CancellationToken cancellationToken)
    {
        Assert.Equal(2, solution.ProjectIds.Count);
        Assert.Equal(2, solution.Projects.Select(project => project.FilePath).Distinct().Count());
        var baselines = new Dictionary<ProjectId, CSharpCompilation>();
        foreach (var project in solution.Projects)
        {
            var baseline = Assert.IsType<CSharpCompilation>(await project.GetCompilationAsync(cancellationToken));
            Assert.Empty(await project.GetSourceGeneratedDocumentsAsync(cancellationToken));
            var executed = false;
            Assert.Equal("supported-query", CapabilityOperationGate.Execute(baseline, "selected-symbols", true,
                () => executed = true, () => Assert.Fail("The ordinary real control requires signing fallback."),
                _ => Assert.Fail("The ordinary real control did not pass the bounded signing check.")));
            Assert.True(executed);
            baselines.Add(project.Id, baseline);
        }
        using var factory = new AdhocWorkspace();
        var reconstruction = await RealControlReconstruction.CreateAsync(solution, descriptors, factory, cancellationToken);
        var reconstructed = reconstruction.Compilations;
        foreach (var project in solution.Projects)
        {
            var restored = reconstructed[project.Id];
            Assert.Equal(project.MetadataReferences.Count + project.ProjectReferences.Count(), restored.References.Count());
            Assert.Equal(project.Documents.Count(), restored.SyntaxTrees.Count());
            Assert.Equal(baselines[project.Id].Assembly.Identity, restored.Assembly.Identity);
            Assert.Empty(restored.GetDiagnostics(cancellationToken).Where(item => item.Severity == DiagnosticSeverity.Error));
            Assert.Equal(baselines[project.Id].GetDiagnostics(cancellationToken).Select(item => item.ToString()).Order(),
                restored.GetDiagnostics(cancellationToken).Select(item => item.ToString()).Order());
            var candidateProject = Assert.Single(reconstruction.Solution.Projects, item => item.FilePath == project.FilePath);
            Assert.NotEqual(project.Id, candidateProject.Id);
            Assert.Equal(project.AnalyzerReferences.Select(item => item.FullPath),
                candidateProject.AnalyzerReferences.Select(item => item.FullPath));
            Assert.Equal(project.AnalyzerConfigDocuments.Select(item => item.FilePath),
                candidateProject.AnalyzerConfigDocuments.Select(item => item.FilePath));
            var descriptor = Assert.Single(descriptors, item => item.FilePath == project.FilePath);
            var candidateDescriptor = Assert.Single(reconstruction.Descriptors, item => item.FilePath == project.FilePath);
            Assert.Equal(descriptor.Documents.Select(item => (item.FilePath, item.IsGenerated, item.SourceCodeKind)),
                candidateDescriptor.Documents.Select(item => (item.FilePath, item.IsGenerated, item.SourceCodeKind)));
            foreach (var reference in project.MetadataReferences)
            {
                var file = Assert.IsAssignableFrom<PortableExecutableReference>(reference);
                var candidate = Assert.Single(restored.References.OfType<PortableExecutableReference>(),
                    item => item.FilePath == file.FilePath && item.Properties.Equals(file.Properties));
                Assert.Equal(baselines[project.Id].GetAssemblyOrModuleSymbol(reference)?.ToDisplayString(),
                    restored.GetAssemblyOrModuleSymbol(candidate)?.ToDisplayString());
            }
        }
        AssertSelectedSymbols(baselines[pluginId]);
        AssertSelectedSymbols(reconstructed[pluginId]);
        AssertInMemoryEdit(baselines[pluginId]);
        AssertInMemoryEdit(reconstructed[pluginId]);
        var serverId = Assert.Single(solution.GetProject(pluginId)!.ProjectReferences).ProjectId;
        var originalContract = baselines[serverId].GetTypeByMetadataName("RoslynMcpServer.Plugins.IRoslynMcpPlugin")!;
        var restoredContract = reconstructed[serverId].GetTypeByMetadataName("RoslynMcpServer.Plugins.IRoslynMcpPlugin")!;
        var originalXml = originalContract.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        Assert.Contains("A plugin compiled against the host assembly", originalXml);
        Assert.Equal(originalXml, restoredContract.GetDocumentationCommentXml(cancellationToken: cancellationToken));
        var originalExternalXml = baselines[pluginId].GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken")!
            .GetDocumentationCommentXml(cancellationToken: cancellationToken);
        Assert.False(string.IsNullOrEmpty(originalExternalXml), "The XML exclusion requires a nonempty ordinary witness.");
        Assert.True(string.IsNullOrEmpty(reconstructed[pluginId].GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken")!
            .GetDocumentationCommentXml(cancellationToken: cancellationToken)));
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(new
        {
            Scope = "same-process public descriptor/workspace reconstruction and selected base compiler queries",
            Projects = reconstructed.Count,
            MetadataReferences = solution.Projects.Sum(project => project.MetadataReferences.Count),
            DirectedProjectReferences = solution.Projects.Sum(project => project.ProjectReferences.Count()),
            SelectedSymbols = new[] { "RoslynMcpPlugin.SamplePlugin", "IRoslynMcpPlugin", "JToken.FromObject", "McpServerToolAttribute" },
            SourceDocumentationEqual = true,
            ExternalDocumentationLossObserved = true,
            InMemoryEditConstant = "capability-edited",
            PublicMetadataResolverFactoryEqual = true,
            StrongNamePolicy = "declared empty search paths; signing/emit excluded by H-002",
            MetadataDocumentationPolicy = "DocumentationProvider.Default; H-001",
            Capability = "supported for the observed base-query slice; no cross-process codec or production admission",
            Documents = descriptors.Select(info => new
            {
                info.FilePath,
                Documents = info.Documents.Select(document => new
                {
                    document.FilePath,
                    document.IsGenerated,
                    document.SourceCodeKind,
                    document.Folders,
                }),
                AnalyzerConfigs = info.AnalyzerConfigDocuments.Select(document => document.FilePath),
            }),
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static void AssertInMemoryEdit(CSharpCompilation compilation)
    {
        var tree = Assert.Single(compilation.SyntaxTrees, item => Path.GetFileName(item.FilePath) == "SamplePlugin.cs");
        var root = tree.GetRoot();
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Name");
        var value = Assert.IsType<LiteralExpressionSyntax>(property.ExpressionBody!.Expression);
        var editedRoot = root.ReplaceNode(value, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal("capability-edited")).WithTriviaFrom(value));
        var editedTree = tree.WithRootAndOptions(editedRoot, tree.Options);
        var edited = compilation.ReplaceSyntaxTree(tree, editedTree);
        var expression = editedTree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Name").ExpressionBody!.Expression;
        Assert.Equal("capability-edited", edited.GetSemanticModel(editedTree).GetConstantValue(expression).Value);
        Assert.Empty(edited.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
    }

    private static void AssertSelectedSymbols(CSharpCompilation compilation)
    {
        var plugin = compilation.GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin");
        Assert.NotNull(plugin);
        var contract = Assert.Single(plugin.Interfaces);
        Assert.Equal("RoslynMcpServer.Plugins.IRoslynMcpPlugin", contract.ToDisplayString());
        Assert.Equal("RoslynMcpServer", contract.ContainingAssembly.Name);
        var tree = Assert.Single(compilation.SyntaxTrees,
            item => Path.GetFileName(item.FilePath) == "SampleTools.cs");
        var model = compilation.GetSemanticModel(tree);
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "FromObject" });
        var method = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(call).Symbol);
        Assert.Equal("Newtonsoft.Json.Linq.JToken", method.ContainingType.ToDisplayString());
        Assert.Equal("Newtonsoft.Json", method.ContainingAssembly.Name);
        var tools = compilation.GetTypeByMetadataName("RoslynMcpPlugin.SampleTools")!;
        var operation = Assert.Single(tools.GetMembers("LoadedWorkspace").OfType<IMethodSymbol>());
        Assert.Contains(operation.GetAttributes(), attribute => attribute.AttributeClass?.ToDisplayString()
            == "ModelContextProtocol.Server.McpServerToolAttribute");
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
        var props = Path.GetFullPath(Path.Combine(RepositoryRoot.Find(), "Directory.Build.props"));
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

    private static void CheckLinkedRolesAndProducerAmbiguity(WorkspaceSnapshot snapshot, string root)
    {
        var first = snapshot.Projects[0];
        var second = snapshot.Projects[1];
        var linked = first.Documents.First(document => document.Kind == "source") with
        {
            Path = Path.Combine(root, "shared-input.cs"),
            IsGenerated = false,
        };
        var binaryPath = Path.Combine(root, "shared-output.dll");
        first = first with
        {
            OutputFilePath = binaryPath,
            OutputRefFilePath = null,
            Documents = [linked, linked with { Kind = "additional" }],
            MetadataReferences = [new(binaryPath, "Assembly", ["alias"], true, "H-001-external-xml-excluded")],
            ProjectReferences = [new(second.Key, [], false)],
        };
        second = second with { OutputFilePath = binaryPath, OutputRefFilePath = null, Documents = [linked] };
        var paths = RealControlSchemaCapture.CaptureInputPaths([first, second]);
        var occurrences = paths.Single(input => input.Path == linked.Path).Occurrences;
        Assert.Equal(3, occurrences.Length);
        Assert.Equal(2, occurrences.Select(occurrence => occurrence.Owner).Distinct().Count());
        Assert.Contains(occurrences, occurrence => occurrence.Owner == first.Key && occurrence.Role == "AdditionalFile");
        Assert.All(paths.Single(input => input.Path == binaryPath).Occurrences
            .Where(occurrence => occurrence.Role == "MetadataDependency"), occurrence => Assert.Null(occurrence.Producer));

        // A unique matching output is still insufficient without the actual directed binding.
        first = first with { OutputFilePath = null, ProjectReferences = [] };
        Assert.Null(Assert.Single(RealControlSchemaCapture.CaptureInputPaths([first, second])
            .Single(input => input.Path == binaryPath).Occurrences,
            occurrence => occurrence.Role == "MetadataDependency").Producer);
        first = first with { ProjectReferences = [new(second.Key, [], false)] };
        Assert.Equal(second.Key, Assert.Single(RealControlSchemaCapture.CaptureInputPaths([first, second])
            .Single(input => input.Path == binaryPath).Occurrences,
            occurrence => occurrence.Role == "MetadataDependency").Producer);
    }

    private static void AssertText(SourceText expected, SourceText actual)
    {
        Assert.True(expected.ContentEquals(actual));
        Assert.Equal(expected.Encoding?.CodePage, actual.Encoding?.CodePage);
        Assert.Equal(expected.Encoding?.GetPreamble() ?? [], actual.Encoding?.GetPreamble() ?? []);
        Assert.Equal(expected.ChecksumAlgorithm, actual.ChecksumAlgorithm);
        Assert.Equal(expected.GetChecksum().ToArray(), actual.GetChecksum().ToArray());
    }

    private static async Task RestoreAndCheckAsync(string directory, CancellationToken cancellationToken)
    {
        var snapshot = WorkspaceSnapshotCodec.Decode(await File.ReadAllBytesAsync(
            Path.Combine(directory, "snapshot.bin"), cancellationToken));
        Assert.Equal(RoundtripObservation.Properties(), snapshot.Properties);
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, cancellationToken);
        Assert.Empty(session.Workspace.CurrentSolution.ProjectIds);
        var actual = await RoundtripObservation.ReadAsync(session.Solution, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "hydrated.json"), actual, cancellationToken);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "ordinary.json"), cancellationToken), actual);
        var oldIds = (await File.ReadAllLinesAsync(Path.Combine(directory, "ordinary.ids"), cancellationToken)).ToHashSet();
        Assert.All(session.Solution.ProjectIds, id => Assert.DoesNotContain(id.Id.ToString(), oldIds));
        Assert.All(session.Solution.Projects.SelectMany(RoundtripObservation.AllDocuments),
            document => Assert.DoesNotContain(document.Id.Id.ToString(), oldIds));
        RoundtripObservation.AssertBindings(snapshot, session);
        var plugin = session.BaseCompilations[session.Projects.Single(pair =>
            pair.Key.ProjectPath.EndsWith("RoslynMcpPlugin.csproj", StringComparison.Ordinal)).Value];
        var sample = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin"));
        Assert.Contains(sample.AllInterfaces, symbol => symbol.ToDisplayString() == "RoslynMcpServer.Plugins.IRoslynMcpPlugin"
            && symbol.ContainingAssembly.Name == "RoslynMcpServer");
        Assert.EndsWith(Path.Combine("RoslynMcpPlugin", "SamplePlugin.cs"), Assert.Single(sample.Locations).SourceTree!.FilePath);
        var external = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken"));
        Assert.Equal("", external.GetDocumentationCommentXml(cancellationToken: cancellationToken));
        Assert.DoesNotContain(plugin.GetDiagnostics(cancellationToken), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var server = session.BaseCompilations[session.Projects.Single(pair =>
            Path.GetFileName(pair.Key.ProjectPath) == "RoslynMcpServer.csproj").Value];
        Assert.Contains("Portable graph facts only", server
            .GetTypeByMetadataName("RoslynMcpServer.Services.Workspace.StateCache.Models.WorkspaceSnapshot")!
            .GetDocumentationCommentXml(cancellationToken: cancellationToken), StringComparison.Ordinal);
        Assert.Equal(NullableContextOptions.Enable, plugin.Options.NullableContextOptions);
        Assert.Equal(OptimizationLevel.Debug, plugin.Options.OptimizationLevel);
        AssertExclusions(plugin);
        await RoundtripProcess.WriteReceiptAsync(directory, cancellationToken);
    }

    private static void AssertExclusions(CSharpCompilation compilation)
    {
        foreach (var operation in new[] { "metadata-documentation", "documentation-analyzer", "emit", "sign", "unknown-write" })
        {
            var executed = false;
            var logs = new List<string>();
            var reason = CapabilityOperationGate.Execute(compilation, operation, true, () => executed = true, null, logs.Add);
            Assert.False(executed);
            Assert.Contains(operation.StartsWith("metadata", StringComparison.Ordinal) || operation == "documentation-analyzer"
                ? "excluded-metadata-documentation" : operation == "unknown-write" ? "signing-state-unknown" : "excluded-emit-signing", reason);
            Assert.Contains("route=refusal", Assert.Single(logs), StringComparison.Ordinal);
        }
    }
}
