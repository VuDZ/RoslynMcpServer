using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class TestImpactCompilationErrorsTests
{
    [Theory]
    [InlineData("class A {", "class B {}", false, "CS1513")]
    [InlineData("class A { void M() { Missing(); } }", "class B {}", false, "CS0103")]
    [InlineData("class Duplicate {}", "class Duplicate {}", false, "CS0101")]
    [InlineData("class A { int M() => B.Removed(); }", "class B { }", true, "CS0117")]
    public async Task Changed_file_errors_match_full_compilation(
        string changedSource, string neighboringSource, bool bothChanged, string errorId)
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "App", out var project);
        // Declare the neighboring file first so the duplicate declaration is attributed to the changed file.
        solution = AddDocument(solution, project, "Neighbor.cs", neighboringSource, out var neighboring);
        solution = AddDocument(solution, project, "Changed.cs", changedSource, out var changed);
        solution = AddDocument(solution, project, "Unchanged.cs", "class Unchanged { void M() => OtherError(); }", out _);
        await AssertMatchesFullCompilationAsync(solution, bothChanged ? [changed, neighboring] : [changed], errorId);
    }

    [Fact]
    public async Task All_loaded_target_contexts_contribute_errors_for_the_same_file()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "App (net8.0)", out var first);
        solution = AddProject(solution, "App (net10.0)", out var second)
            .WithProjectParseOptions(first, CSharpParseOptions.Default.WithPreprocessorSymbols("NET8_0"));
        const string source = """
            class Changed
            {
            #if NET8_0
                void M() => MissingInNet8();
            #else
                void M() => MissingInNet10();
            #endif
            }
            """;
        solution = AddDocument(solution, first, "Shared.cs", source, out var path);
        solution = AddDocument(solution, second, "Shared.cs", source, out _);
        var errors = await AssertMatchesFullCompilationAsync(solution, [path], "CS0103");
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains("MissingInNet8", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MissingInNet10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generated_neighbor_participates_in_binding_with_its_own_errors_excluded()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "App", out var project)
            .AddAnalyzerReference(project, new GeneratorReference());
        solution = AddDocument(solution, project, "Changed.cs", "class Changed { int M() => Generated.WrongMember; }", out var path);
        var loaded = solution.GetProject(project)!;
        Assert.Single(await loaded.GetSourceGeneratedDocumentsAsync());
        var errors = await AssertMatchesFullCompilationAsync(solution, [path], "CS0117");
        Assert.Single(errors);
        var compilation = Assert.IsAssignableFrom<Compilation>(await loaded.GetCompilationAsync());
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0103"
            && diagnostic.Location.SourceTree?.FilePath.EndsWith("Generated.g.cs", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Project_entry_point_conflicts_are_left_to_the_build()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "App", out var project)
            .WithProjectCompilationOptions(project, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        solution = AddDocument(solution, project, "Changed.cs", "class A { static void Main() {} }", out var path);
        solution = AddDocument(solution, project, "Neighbor.cs", "class B { static void Main() {} }", out _);
        var changedPaths = new HashSet<string>(InputPathCanon.Comparer) { path };

        var errors = await TestImpactEstimator.CompilationErrorsAsync(solution, changedPaths, CancellationToken.None);
        var compilation = Assert.IsAssignableFrom<Compilation>(await solution.GetProject(project)!.GetCompilationAsync());
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0017"
            && diagnostic.Location.SourceTree?.FilePath == path);
        Assert.Empty(errors);
    }

    private static async Task<IReadOnlyList<string>> AssertMatchesFullCompilationAsync(
        Solution solution, string[] paths, string errorId)
    {
        var changedPaths = paths.ToHashSet(InputPathCanon.Comparer);
        var actual = await TestImpactEstimator.CompilationErrorsAsync(solution, changedPaths, CancellationToken.None);
        var expected = new List<string>();
        foreach (var project in solution.Projects)
        {
            var compilation = Assert.IsAssignableFrom<Compilation>(await project.GetCompilationAsync());
            expected.AddRange(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Location.SourceTree is { } tree && changedPaths.Contains(tree.FilePath))
                .Select(diagnostic => diagnostic.ToString()));
        }

        Assert.Contains(expected, error => error.Contains(errorId, StringComparison.Ordinal));
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        return actual;
    }

    private static Solution AddProject(Solution solution, string name, out ProjectId id)
    {
        id = ProjectId.CreateNewId();
        return solution.AddProject(ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            "App",
            LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "App.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
    }

    private static Solution AddDocument(Solution solution, ProjectId project, string name, string source, out string path)
    {
        path = Path.Combine(Path.GetTempPath(), name);
        return solution.AddDocument(DocumentId.CreateNewId(project), name, SourceText.From(source), filePath: path);
    }

    private sealed class GeneratorReference : AnalyzerReference
    {
        public override string? FullPath => null;

        public override object Id { get; } = new object();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => _generators;

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => _generators;

        private readonly ImmutableArray<ISourceGenerator> _generators = [new NeighborGenerator().AsSourceGenerator()];
    }

    private sealed class NeighborGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(static output =>
                output.AddSource("Generated.g.cs", "class Generated { static int Value => MissingInGenerator; }"));
    }
}
