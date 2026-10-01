using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

/// <summary>
/// A saved edit of an additional file or analyzer config that is already in the loaded project
/// stays invisible after the document text has been read. Replacing that text in memory changes
/// the next compilation and does not write the project file. <see cref="Workspace.TryApplyChanges"/>
/// persists an additional-file edit by rewriting that file, and throws for an analyzer-config edit.
/// The published snapshot is left on the text from load.
/// </summary>
public sealed class NonCSharpDocumentTextSyncSpikeTests
{
    [Fact]
    public async Task Additional_file_text_changes_generated_marker_without_rewriting_csproj()
    {
        await using var project = await SpikeProject.CreateAsync(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <AdditionalFiles Include="payload.txt" />
              </ItemGroup>
            </Project>
            """,
            ("Class1.cs", "namespace App; public static class Class1 { }"),
            ("payload.txt", "OFF"));

        var loaded = project.Manager.GetWorkspaceCurrentSolution();
        Assert.NotNull(loaded);
        var additional = loaded.Projects.Single().AdditionalDocuments.SingleOrDefault(document =>
            document.FilePath?.EndsWith("payload.txt", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(additional is not null, project.Describe(loaded));

        var initialText = await loaded.GetAdditionalDocument(additional.Id)!.GetTextAsync();
        Assert.Equal("OFF", initialText.ToString());
        Assert.Contains("Marker = \"Off\"", await PayloadMarkerFromSolutionAsync(loaded, additional.Id), StringComparison.Ordinal);

        Assert.Contains(
            "Marker = \"On\"",
            await PayloadMarkerAsync(loaded, additional.Id, "ON"),
            StringComparison.Ordinal);
        await project.AssertFilesUnchangedAsync();

        // The text is frozen once read. A later save stays invisible until the snapshot is replaced.
        await File.WriteAllTextAsync(project.FilePath("payload.txt"), "ON", BomFreeUtf8);
        var textAfterDiskWrite = await loaded.GetAdditionalDocument(additional.Id)!.GetTextAsync();
        var markerAfterDiskWrite = await PayloadMarkerFromSolutionAsync(loaded, additional.Id);
        Assert.Equal("OFF", textAfterDiskWrite.ToString());
        Assert.Contains("Marker = \"Off\"", markerAfterDiskWrite, StringComparison.Ordinal);
        await File.WriteAllTextAsync(project.FilePath("payload.txt"), "OFF", BomFreeUtf8);

        var publishedBefore = await project.Manager.GetPublishedSolutionAsync();
        Assert.NotNull(publishedBefore);
        var updated = loaded.WithAdditionalDocumentText(additional.Id, SourceText.From("ON", BomFreeUtf8));
        Assert.True(loaded.Workspace.TryApplyChanges(updated));
        await project.AssertProjectFileUnchangedAsync();
        Assert.Equal("ON", (await File.ReadAllTextAsync(project.FilePath("payload.txt"))).Trim());

        var workspaceAfter = project.Manager.GetWorkspaceCurrentSolution();
        var publishedAfter = await project.Manager.GetPublishedSolutionAsync();
        Assert.NotNull(workspaceAfter);
        Assert.NotNull(publishedAfter);
        Assert.False(ReferenceEquals(workspaceAfter, publishedAfter));
        Assert.True(ReferenceEquals(publishedBefore, publishedAfter));
        Assert.Equal(
            "ON",
            (await workspaceAfter.GetAdditionalDocument(additional.Id)!.GetTextAsync()).ToString());
        Assert.Equal(
            "OFF",
            (await publishedAfter.GetAdditionalDocument(additional.Id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task Analyzer_config_text_changes_unused_variable_diagnostic_without_rewriting_csproj()
    {
        const string Suppressed = """
            root = true

            [*.cs]
            dotnet_diagnostic.CS0219.severity = none
            """;
        const string Reported = """
            root = true

            [*.cs]
            dotnet_diagnostic.CS0219.severity = error
            """;

        await using var project = await SpikeProject.CreateAsync(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            ("Class1.cs", """
                namespace App;
                public static class Class1
                {
                    public static int M()
                    {
                        int unused = 1;
                        return 0;
                    }
                }
                """),
            (".editorconfig", Suppressed));

        var loaded = project.Manager.GetWorkspaceCurrentSolution();
        Assert.NotNull(loaded);
        var config = loaded.Projects.Single().AnalyzerConfigDocuments.SingleOrDefault(document =>
            string.Equals(document.FilePath, project.FilePath(".editorconfig"), StringComparison.OrdinalIgnoreCase));
        Assert.True(config is not null, project.Describe(loaded));

        var initialText = await loaded.GetAnalyzerConfigDocument(config.Id)!.GetTextAsync();
        Assert.Contains("severity = none", initialText.ToString(), StringComparison.Ordinal);
        Assert.Empty(await UnusedVariableDiagnosticsFromSolutionAsync(loaded, config.Id));

        var reported = await UnusedVariableDiagnosticsAsync(loaded, config.Id, Reported);
        Assert.Contains(reported, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await project.AssertFilesUnchangedAsync();

        await File.WriteAllTextAsync(project.FilePath(".editorconfig"), Reported, BomFreeUtf8);
        var textAfterDiskWrite = await loaded.GetAnalyzerConfigDocument(config.Id)!.GetTextAsync();
        var diagnosticsAfterDiskWrite = await UnusedVariableDiagnosticsFromSolutionAsync(loaded, config.Id);
        Assert.Contains("severity = none", textAfterDiskWrite.ToString(), StringComparison.Ordinal);
        Assert.Empty(diagnosticsAfterDiskWrite);
        await File.WriteAllTextAsync(project.FilePath(".editorconfig"), Suppressed, BomFreeUtf8);

        var updated = loaded.WithAnalyzerConfigDocumentText(config.Id, SourceText.From(Reported, BomFreeUtf8));
        var applyFailure = Assert.Throws<NotSupportedException>(() => loaded.Workspace.TryApplyChanges(updated));
        Assert.Contains("analyzer config", applyFailure.Message, StringComparison.OrdinalIgnoreCase);
        await project.AssertFilesUnchangedAsync();
    }

    private static Task<string> PayloadMarkerAsync(Solution solution, DocumentId additionalId, string payload)
    {
        var withPayload = solution.WithAdditionalDocumentText(
            additionalId,
            SourceText.From(payload, BomFreeUtf8));
        return PayloadMarkerFromSolutionAsync(withPayload, additionalId);
    }

    private static async Task<string> PayloadMarkerFromSolutionAsync(Solution solution, DocumentId additionalId)
    {
        var project = solution.GetProject(additionalId.ProjectId);
        Assert.NotNull(project);
        project = project.AddAnalyzerReference(
            new InMemoryGeneratorReference(new PayloadGenerator().AsSourceGenerator()));
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var generated = compilation.SyntaxTrees.FirstOrDefault(tree =>
            tree.ToString().Contains("class Payload", StringComparison.Ordinal));
        Assert.True(
            generated is not null,
            string.Join(Environment.NewLine, compilation.GetDiagnostics().Select(diagnostic => diagnostic.ToString())));
        return generated.ToString();
    }

    private static Task<IReadOnlyList<Diagnostic>> UnusedVariableDiagnosticsAsync(
        Solution solution,
        DocumentId configId,
        string configText)
    {
        var updated = solution.WithAnalyzerConfigDocumentText(configId, SourceText.From(configText, BomFreeUtf8));
        return UnusedVariableDiagnosticsFromSolutionAsync(updated, configId);
    }

    private static async Task<IReadOnlyList<Diagnostic>> UnusedVariableDiagnosticsFromSolutionAsync(
        Solution solution,
        DocumentId configId)
    {
        var project = solution.GetProject(configId.ProjectId);
        Assert.NotNull(project);
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == "CS0219" && diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
    }

    private sealed class SpikeProject : IAsyncDisposable
    {
        public SolutionManager Manager { get; }

        public string Root { get; }

        private SpikeProject(SolutionManager manager, string root, Dictionary<string, byte[]> files)
        {
            Manager = manager;
            Root = root;
            _files = files;
        }

        public static async Task<SpikeProject> CreateAsync(
            string projectXml,
            params (string Name, string Text)[] files)
        {
            MsBuildBootstrapper.Register();
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpNonCSharpSpike-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var written = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            await WriteAsync(root, "App.csproj", projectXml, written);
            foreach (var (name, text) in files)
            {
                await WriteAsync(root, name, text, written);
            }

            var manager = SolutionManagerTestFactory.Create();
            await manager.LoadAndPrepareAsync(
                Path.Combine(root, "App.csproj"),
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None);
            return new SpikeProject(manager, root, written);
        }

        public string FilePath(string name) => Path.Combine(Root, name);

        public async Task AssertFilesUnchangedAsync()
        {
            foreach (var (name, bytes) in _files)
            {
                Assert.Equal(bytes, await File.ReadAllBytesAsync(FilePath(name)));
            }
        }

        public async Task AssertProjectFileUnchangedAsync()
        {
            Assert.Equal(_files["App.csproj"], await File.ReadAllBytesAsync(FilePath("App.csproj")));
        }

        public string Describe(Solution solution)
        {
            var project = solution.Projects.Single();
            var additional = string.Join(", ", project.AdditionalDocuments.Select(document => document.FilePath));
            var configs = string.Join(", ", project.AnalyzerConfigDocuments.Select(document => document.FilePath));
            var diagnostics = string.Join(
                Environment.NewLine,
                Manager.LastDiagnostics.Select(diagnostic => diagnostic.Kind + ": " + diagnostic.Message));
            return "additional=[" + additional + "] configs=[" + configs + "]" + Environment.NewLine + diagnostics;
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.ClearWorkspaceAsync();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // The design-time build can keep a short-lived lock on obj.
            }
        }

        private static async Task WriteAsync(
            string root,
            string name,
            string text,
            Dictionary<string, byte[]> written)
        {
            var path = Path.Combine(root, name);
            await File.WriteAllTextAsync(path, text, BomFreeUtf8);
            written[name] = await File.ReadAllBytesAsync(path);
        }

        private readonly Dictionary<string, byte[]> _files;
    }

    private sealed class InMemoryGeneratorReference : AnalyzerReference
    {
        public override string Display => "In-memory payload generator";

        public override string? FullPath => null;

        public override object Id { get; } = new object();

        public InMemoryGeneratorReference(ISourceGenerator generator)
        {
            _generators = ImmutableArray.Create(generator);
        }

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) =>
            ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() =>
            ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => _generators;

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => _generators;

        private readonly ImmutableArray<ISourceGenerator> _generators;
    }

    private sealed class PayloadGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var payload = context.AdditionalTextsProvider
                .Where(static text => text.Path?.EndsWith("payload.txt", StringComparison.OrdinalIgnoreCase) == true)
                .Select(static (text, cancellationToken) => text.GetText(cancellationToken)?.ToString() ?? "");
            context.RegisterSourceOutput(payload, static (output, content) =>
            {
                var marker = content.Contains("ON", StringComparison.Ordinal) ? "On" : "Off";
                output.AddSource(
                    "Payload.g.cs",
                    "namespace App;\ninternal static class Payload\n{\n    public const string Marker = \"" + marker + "\";\n}\n");
            });
        }
    }

    private static readonly UTF8Encoding BomFreeUtf8 = new(encoderShouldEmitUTF8Identifier: false);
}
