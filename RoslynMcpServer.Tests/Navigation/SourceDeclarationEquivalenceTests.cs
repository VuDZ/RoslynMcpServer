using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Navigation;
using Xunit;

namespace RoslynMcpServer.Tests.Navigation;

public sealed class SourceDeclarationEquivalenceTests
{
    [Theory]
    [InlineData("Widget", SymbolFilter.Type)]
    [InlineData("widget", SymbolFilter.Type)]
    [InlineData("Run", SymbolFilter.Member)]
    [InlineData("Sample.Widget.Run", SymbolFilter.TypeAndMember)]
    [InlineData("global::Sample.Widget.Run()", SymbolFilter.TypeAndMember)]
    [InlineData("Value", SymbolFilter.Member)]
    [InlineData("Nested", SymbolFilter.Type)]
    [InlineData("SharedType", SymbolFilter.Type)]
    [InlineData("GeneratedWidget", SymbolFilter.Type)]
    [InlineData("GeneratedValue", SymbolFilter.Member)]
    [InlineData("String", SymbolFilter.Type)]
    [InlineData("Missing", SymbolFilter.TypeAndMember)]
    public async Task Source_search_matches_legacy_source_results_on_fresh_workspaces(string query, SymbolFilter filter)
    {
        using var legacyWorkspace = new AdhocWorkspace();
        using var sourceWorkspace = new AdhocWorkspace();
        var legacySolution = CreateSolution(legacyWorkspace);
        var sourceSolution = CreateSolution(sourceWorkspace);
        Assert.Single(await legacySolution.Projects.First().GetSourceGeneratedDocumentsAsync());
        Assert.Single(await sourceSolution.Projects.First().GetSourceGeneratedDocumentsAsync());
        var normalized = SymbolDeclarationResolver.NormalizeFqn(query);
        var simpleName = normalized[(normalized.LastIndexOf('.') + 1)..];
        var legacy = new List<ISymbol>();
        foreach (var project in legacySolution.Projects)
        {
            legacy.AddRange(await SymbolFinder.FindDeclarationsAsync(project, simpleName, true, filter));
        }
        var expected = legacy.Where(symbol => symbol.Locations.Any(location => location.IsInSource))
            .Distinct(SymbolEqualityComparer.Default);
        if (normalized.Contains('.'))
        {
            expected = expected.Where(symbol => SymbolDeclarationResolver.GetSymbolFqn(symbol) == normalized);
        }

        var actual = await SymbolDeclarationResolver.ResolveDeclarationsAsync(sourceSolution, query, filter);
        Assert.Null(actual.Error);
        Assert.Equal(Identities(expected), Identities(actual.Symbols));
        if (query == "SharedType")
        {
            Assert.Equal(2, actual.Symbols.Count);
        }
    }

    [Fact]
    public async Task Fqn_miss_keeps_candidates_and_does_not_fall_back()
    {
        using var workspace = new AdhocWorkspace();
        var actual = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            CreateSolution(workspace), "Missing.Widget", SymbolFilter.Type);
        Assert.Empty(actual.Symbols);
        Assert.Contains("`Sample.Widget`", actual.Error);
        Assert.Contains("Did not fall back", actual.Error);
    }

    private static string[] Identities(IEnumerable<ISymbol> symbols) => symbols.Select(symbol =>
            symbol.ContainingAssembly?.Name + "|" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "|" + string.Join(";", symbol.Locations.Where(location => location.IsInSource)
                .Select(location => location.SourceTree!.FilePath + ":" + location.SourceSpan)))
        .Order(StringComparer.Ordinal).ToArray();

    private static Solution CreateSolution(AdhocWorkspace workspace)
    {
        var dependency = ProjectId.CreateNewId();
        var consumer = ProjectId.CreateNewId();
        var root = Path.Combine(Path.GetTempPath(), "SourceDeclarationEquivalence");
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(dependency, VersionStamp.Create(), "Dependency", "Dependency", LanguageNames.CSharp,
                filePath: Path.Combine(root, "Dependency.csproj")))
            .AddProject(ProjectInfo.Create(consumer, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp,
                filePath: Path.Combine(root, "Consumer.csproj")))
            .AddProjectReference(consumer, new ProjectReference(dependency))
            .AddMetadataReference(dependency, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddMetadataReference(consumer, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddAnalyzerReference(dependency, new GeneratorReference())
            .AddDocument(DocumentId.CreateNewId(dependency), "Widget.cs", SourceText.From("""
                namespace Sample;
                public interface IRunner { void Run(); }
                public partial class Widget : IRunner
                {
                    public void Run() { }
                    public void Run(int value) { }
                    public class Nested { }
                }
                public partial class Widget { public int Value { get; set; } }
                """), filePath: Path.Combine(root, "Widget.cs"))
            .AddDocument(DocumentId.CreateNewId(consumer), "Consumer.cs",
                SourceText.From("class Consumer : Sample.Widget { }"), filePath: Path.Combine(root, "Consumer.cs"));
        foreach (var project in new[] { dependency, consumer })
        {
            solution = solution.AddDocument(DocumentId.CreateNewId(project), "Linked.cs",
                SourceText.From("namespace Shared; public class SharedType { }"), filePath: Path.Combine(root, "Linked.cs"));
        }
        return solution;
    }

    private sealed class GeneratorReference : AnalyzerReference
    {
        public override string FullPath => "equivalence-generator";
        public override object Id => FullPath;
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => [new WidgetGenerator().AsSourceGenerator()];
        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => [new WidgetGenerator().AsSourceGenerator()];
    }

    private sealed class WidgetGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(static output => output.AddSource("GeneratedWidget.g.cs",
                "namespace Generated { public class GeneratedWidget { public int GeneratedValue => 42; } }"));
    }
}
