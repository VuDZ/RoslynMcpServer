using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SourcePositionHelperTests
{
    [Fact]
    public async Task ResolveSymbol_on_declaration_returns_that_symbol()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public void DoWork() { }
            }
            """);

        var (symbol, position, error) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "DoWork",
            line1Based: 5,
            column1Based: null);

        Assert.Null(error);
        Assert.NotNull(position);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Method, symbol!.Kind);
        Assert.Equal("DoWork", symbol.Name);
        Assert.Equal("Widget", symbol.ContainingType?.Name);
    }

    [Fact]
    public async Task ResolveSymbol_on_usage_returns_same_method()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public void DoWork() { }

                public void Run()
                {
                    DoWork();
                }
            }
            """);

        var (declSymbol, _, declError) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "DoWork",
            line1Based: 5,
            column1Based: null);
        Assert.Null(declError);

        var (usageSymbol, _, usageError) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "DoWork",
            line1Based: 9,
            column1Based: null);
        Assert.Null(usageError);
        Assert.NotNull(declSymbol);
        Assert.NotNull(usageSymbol);
        Assert.True(SymbolEqualityComparer.Default.Equals(declSymbol, usageSymbol));
    }

    [Fact]
    public async Task AutoColumn_with_two_same_name_identifiers_reports_ambiguity_not_first_IndexOf()
    {
        // Both "x" identifiers on one line: declaration and usage.
        // string.IndexOf would pick the first; auto-column must list both columns.
        using var ambiguous = Fixture.Create(
            """
            namespace Demo;
            public class Widget
            {
                public void Run() { int x = 1; int z = x; }
            }
            """);

        var (symbol, position, error) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ambiguous.Document,
            "x",
            line1Based: 4,
            column1Based: null);

        Assert.Null(symbol);
        Assert.Null(position);
        Assert.NotNull(error);
        Assert.Contains("Ambiguous", error, StringComparison.Ordinal);
        Assert.Contains("columns", error, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"columns \d+, \d+", error);

        var text = await ambiguous.Document.GetTextAsync();
        var root = await ambiguous.Document.GetSyntaxRootAsync();
        Assert.NotNull(root);
        var tokens = root!.DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "x")
            .Select(t => text.Lines.GetLinePosition(t.SpanStart).Character + 1)
            .OrderBy(c => c)
            .ToList();
        Assert.Equal(2, tokens.Count);

        var (usageSymbol, _, usageError) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ambiguous.Document,
            "x",
            line1Based: 4,
            column1Based: tokens[1]);
        Assert.Null(usageError);
        Assert.NotNull(usageSymbol);
        Assert.Equal("x", usageSymbol!.Name);

        var (declAtFirst, _, declError) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ambiguous.Document,
            "x",
            line1Based: 4,
            column1Based: tokens[0]);
        Assert.Null(declError);
        Assert.NotNull(declAtFirst);
        Assert.True(SymbolEqualityComparer.Default.Equals(declAtFirst, usageSymbol));
    }

    [Fact]
    public void TryGetAbsolutePosition_line_past_eof_returns_readable_error()
    {
        var text = SourceText.From("class A {\n}\n");
        var ok = SourcePositionHelper.TryGetAbsolutePosition(text, line1Based: 99, column1Based: 1, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("past the end of the file", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAbsolutePosition_column_past_line_length_returns_readable_error()
    {
        var text = SourceText.From("abc\n");
        var ok = SourcePositionHelper.TryGetAbsolutePosition(text, line1Based: 1, column1Based: 50, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("past the end of line", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoColumn_with_single_match_works_without_column()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public int Value { get; set; }
            }
            """);

        var (symbol, position, error) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "Value",
            line1Based: 5,
            column1Based: null);

        Assert.Null(error);
        Assert.NotNull(position);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Property, symbol!.Kind);
        Assert.Equal("Value", symbol.Name);
        Assert.True(position!.Column >= 1);
    }

    [Fact]
    public async Task TryResolvePosition_does_not_match_identifier_inside_string_via_IndexOf()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;
            public class Widget
            {
                public void Run()
                {
                    var s = "Value";
                    Value = 1;
                }

                public int Value { get; set; }
            }
            """);

        // Line with the string "Value" only — no identifier token named Value.
        var text = await ctx.Document.GetTextAsync();
        var root = await ctx.Document.GetSyntaxRootAsync();
        Assert.NotNull(root);

        var stringLine = -1;
        for (var i = 0; i < text.Lines.Count; i++)
        {
            if (text.Lines[i].ToString().Contains("\"Value\"", StringComparison.Ordinal))
            {
                stringLine = i + 1;
                break;
            }
        }

        Assert.True(stringLine > 0);
        var ok = SourcePositionHelper.TryResolvePosition(
            root!,
            text,
            stringLine,
            "Value",
            column1Based: null,
            out _,
            out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("was not found as an identifier", error, StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(AdhocWorkspace workspace, Document document)
        {
            Workspace = workspace;
            Document = document;
        }

        public AdhocWorkspace Workspace { get; }
        public Document Document { get; }

        public static Fixture Create(string source)
        {
            var workspace = new AdhocWorkspace();
            var info = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "Position.Tests",
                "Position.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "Position.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));

            var filePath = Path.Combine(Path.GetTempPath(), "PositionSample.cs");
            var document = project.AddDocument("PositionSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            var applied = workspace.CurrentSolution.GetDocument(document.Id)
                ?? throw new InvalidOperationException("Document missing after TryApplyChanges.");
            return new Fixture(workspace, applied);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
