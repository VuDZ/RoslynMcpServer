using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// Stage-4: rename_symbol / get_call_graph must not silently pick the first overload.
/// Exercises <see cref="SymbolSelectionHelper"/> and <see cref="CallGraphHelper"/> (AdhocWorkspace).
/// </summary>
public sealed class RenameAndCallGraphDisambiguationTests
{
    private const string TwoOverloadsSource =
        """
        namespace Demo;

        public class Widget
        {
            public void Run() { Other(); }
            public void Run(int x) { UniqueCallee(); }
            public void Other() { }
            public void UniqueCallee() { }
            public void Invoke() { Run(42); }
        }
        """;

    [Fact]
    public async Task Rename_without_position_two_overloads_is_ambiguity_not_first()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);
        var before = await ctx.Document.GetTextAsync();

        var (selection, error) = await SymbolSelectionHelper.TrySelectForRenameAsync(
            ctx.Document,
            "Run",
            line: null,
            column: null);

        Assert.Null(selection);
        Assert.NotNull(error);
        Assert.Contains("Ambiguous", error, StringComparison.Ordinal);
        Assert.Contains("matches 2 declarations", error, StringComparison.Ordinal);
        Assert.Contains("`Demo.Widget.Run`", error, StringComparison.Ordinal);
        Assert.Contains("`line` and `column` together", error, StringComparison.Ordinal);
        Assert.DoesNotContain("if needed", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Affected locations:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Rename applied", error, StringComparison.Ordinal);

        var after = await ctx.Document.GetTextAsync();
        Assert.Equal(before.ToString(), after.ToString());
        Assert.Same(ctx.Workspace.CurrentSolution, ctx.Solution);
    }

    [Fact]
    public async Task CallGraph_without_position_two_overloads_is_ambiguity_not_first()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);

        var (method, error) = await SymbolSelectionHelper.TrySelectMethodForCallGraphAsync(
            ctx.Document,
            "Widget",
            "Run",
            line: null,
            column: null);

        Assert.Null(method);
        Assert.NotNull(error);
        Assert.Contains("Ambiguous", error, StringComparison.Ordinal);
        Assert.Contains("matches 2 declarations", error, StringComparison.Ordinal);
        Assert.Contains("`line` and `column` together", error, StringComparison.Ordinal);
        Assert.DoesNotContain("if needed", error, StringComparison.Ordinal);

        var (graph, buildError) = await CallGraphHelper.TryBuildCallGraphAsync(
            ctx.Solution,
            ctx.Document,
            "Widget",
            "Run",
            maxNodes: 25,
            includeExternalCallees: false,
            line: null,
            column: null,
            CancellationToken.None);

        Assert.Null(graph);
        Assert.NotNull(buildError);
        Assert.Contains("Ambiguous", buildError, StringComparison.Ordinal);
        Assert.Contains("`line` and `column` together", buildError, StringComparison.Ordinal);
        Assert.DoesNotContain("if needed", buildError, StringComparison.Ordinal);
        Assert.DoesNotContain("## Call graph", buildError, StringComparison.Ordinal);
        Assert.DoesNotContain("**Target:**", buildError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_with_column_selects_int_overload_only()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);
        var (line, column) = await FindMethodIdentifierAsync(ctx.Document, parameterCount: 1);

        var (selection, error) = await SymbolSelectionHelper.TrySelectForRenameAsync(
            ctx.Document,
            "Run",
            line,
            column);

        Assert.Null(error);
        Assert.NotNull(selection);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(selection!.Symbol);
        Assert.Single(method.Parameters);
        Assert.Equal(SpecialType.System_Int32, method.Parameters[0].Type.SpecialType);
        Assert.NotNull(selection.AbsolutePosition);
    }

    [Fact]
    public async Task Rename_line_without_column_is_pair_error_and_does_not_apply()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);
        var before = await ctx.Document.GetTextAsync();

        var (selection, error) = await SymbolSelectionHelper.TrySelectForRenameAsync(
            ctx.Document,
            "Run",
            line: 5,
            column: null);

        Assert.Null(selection);
        Assert.NotNull(error);
        Assert.Contains("line` and `column` must be passed together", error, StringComparison.Ordinal);

        var after = await ctx.Document.GetTextAsync();
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public async Task CallGraph_at_call_site_targets_int_overload_not_first_declaration()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);
        var (line, column) = await FindCallSiteIdentifierAsync(ctx.Document, "Run");

        var (graph, error) = await CallGraphHelper.TryBuildCallGraphAsync(
            ctx.Solution,
            ctx.Document,
            "Widget",
            "Run",
            maxNodes: 25,
            includeExternalCallees: false,
            line,
            column,
            CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(graph);
        Assert.Contains("Run(int", graph!.TargetDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Other()", string.Join('\n', graph.Callees.Select(c => c.DisplayName)), StringComparison.Ordinal);
        Assert.Contains(
            graph.Callees,
            c => c.DisplayName.Contains("UniqueCallee", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallGraph_position_on_non_method_is_clear_error()
    {
        using var ctx = Fixture.Create(TwoOverloadsSource);
        var (line, column) = await FindTypeIdentifierAsync(ctx.Document, "Widget");

        var (method, error) = await SymbolSelectionHelper.TrySelectMethodForCallGraphAsync(
            ctx.Document,
            "Widget",
            "Widget",
            line,
            column);

        Assert.Null(method);
        Assert.NotNull(error);
        Assert.Contains("is not a method", error, StringComparison.Ordinal);
    }

    private static async Task<(int Line, int Column)> FindMethodIdentifierAsync(Document document, int parameterCount)
    {
        var root = await document.GetSyntaxRootAsync();
        Assert.NotNull(root);
        var method = root!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m =>
                m.Identifier.ValueText == "Run"
                && m.ParameterList.Parameters.Count == parameterCount);
        var span = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        return (span.Line + 1, span.Character + 1);
    }

    private static async Task<(int Line, int Column)> FindCallSiteIdentifierAsync(Document document, string name)
    {
        var root = await document.GetSyntaxRootAsync();
        Assert.NotNull(root);
        var invoke = root!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "Invoke");
        var invocation = invoke.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == name);
        var id = (IdentifierNameSyntax)invocation.Expression;
        var span = id.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        return (span.Line + 1, span.Character + 1);
    }

    private static async Task<(int Line, int Column)> FindTypeIdentifierAsync(Document document, string name)
    {
        var root = await document.GetSyntaxRootAsync();
        Assert.NotNull(root);
        var type = root!.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == name);
        var span = type.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        return (span.Line + 1, span.Character + 1);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(AdhocWorkspace workspace, Document document, string filePath)
        {
            Workspace = workspace;
            Document = document;
            FilePath = filePath;
            Solution = workspace.CurrentSolution;
        }

        public AdhocWorkspace Workspace { get; }
        public Solution Solution { get; }
        public Document Document { get; }
        public string FilePath { get; }

        public static Fixture Create(string source)
        {
            var workspace = new AdhocWorkspace();
            var info = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "Disambiguation.Tests",
                "Disambiguation.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "Disambiguation.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));

            var filePath = Path.Combine(Path.GetTempPath(), "DisambiguationSample.cs");
            var document = project.AddDocument("DisambiguationSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            var applied = workspace.CurrentSolution.GetDocument(document.Id)
                ?? throw new InvalidOperationException("Document missing after TryApplyChanges.");
            return new Fixture(workspace, applied, filePath);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
