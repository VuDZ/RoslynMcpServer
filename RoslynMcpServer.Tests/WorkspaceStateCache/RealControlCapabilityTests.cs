using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class RealControlCapabilityTests
{
    public RealControlCapabilityTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Inspect_and_compare_real_control_without_claiming_workspace_readiness()
    {
        var root = FindRepositoryRoot();
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

    [Theory]
    [InlineData("emit", "excluded-emit-signing")]
    [InlineData("metadata-documentation", "excluded-metadata-documentation")]
    [InlineData("selected-symbols", "signing-state-unknown")]
    public void Excluded_operations_refuse_when_no_ordinary_route_is_available(string operation, string expectedReason)
    {
        var log = new List<string>();
        Assert.Equal(expectedReason, CapabilityOperationGate.Execute(CSharpCompilation.Create("RefusalControl"),
            operation, false, () => Assert.Fail("A refused operation executed."), null, log.Add));
        Assert.Contains("route=refusal", Assert.Single(log));
        Assert.Contains("reason=" + expectedReason, log[0]);
    }

    [Theory]
    [InlineData("System.Reflection.AssemblyKeyFile(\"company.snk\")", "signing-dependent")]
    [InlineData("signed-friend", "signing-dependent")]
    [InlineData("MissingSigningAttribute", "signing-state-unknown")]
    public void Assembly_attributes_are_checked_before_the_selected_query(string attribute, string expectedReason)
    {
        if (attribute == "signed-friend")
        {
            var publicKey = Convert.ToHexString(typeof(object).Assembly.GetName().GetPublicKey()!);
            attribute = "System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend, PublicKey=" + publicKey + "\")";
        }
        var compilation = CSharpCompilation.Create("AttributeControl",
            [CSharpSyntaxTree.ParseText("[assembly: " + attribute + "]")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var ordinaryCalls = 0;
        Assert.Equal(expectedReason, CapabilityOperationGate.Execute(compilation, "selected-symbols", true,
            () => Assert.Fail("An excluded attribute reached hydrated execution."), () => ordinaryCalls++, _ => { }));
        Assert.Equal(1, ordinaryCalls);
    }

    [Theory]
    [InlineData("selected-symbols", false, false, "signing-state-unknown")]
    [InlineData("selected-symbols", true, true, "signing-dependent")]
    [InlineData("emit", true, false, "excluded-emit-signing")]
    [InlineData("metadata-documentation", true, false, "excluded-metadata-documentation")]
    [InlineData("documentation-analyzer", true, false, "excluded-metadata-documentation")]
    [InlineData("arbitrary-analyzer", true, false, "signing-state-unknown")]
    public void Excluded_operations_route_before_hydrated_execution(
        string operation, bool known, bool signing, string expectedReason)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (signing)
        {
            options = options.WithCryptoKeyFile("company.snk");
        }
        var compilation = CSharpCompilation.Create("ExcludedControl", options: options);
        var events = new List<string>();
        var result = CapabilityOperationGate.Execute(compilation, operation, known,
            () => events.Add("hydrated-side-effect"), () => events.Add("ordinary-started"), events.Add);
        Assert.Equal(expectedReason, result);
        Assert.Equal(2, events.Count);
        Assert.Contains("reason=" + expectedReason, events[0]);
        Assert.Contains("project=ExcludedControl", events[0]);
        Assert.Contains("route=ordinary-load", events[0]);
        Assert.Equal("ordinary-started", events[1]);
        Assert.DoesNotContain("hydrated-side-effect", events);
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("The real control requires the repository checkout.");
    }
}
