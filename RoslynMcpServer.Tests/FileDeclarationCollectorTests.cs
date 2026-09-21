using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class FileDeclarationCollectorTests
{
    [Fact]
    public async Task Collect_finds_property_field_and_event()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public int Count { get; set; }
                public string Name;
                public event System.EventHandler Changed
                {
                    add { }
                    remove { }
                }
            }
            """);

        Assert.Equal(SymbolKind.Property, (await AssertSingleAsync(ctx, "Count")).Symbol.Kind);
        Assert.Equal(SymbolKind.Field, (await AssertSingleAsync(ctx, "Name")).Symbol.Kind);
        Assert.Equal(SymbolKind.Event, (await AssertSingleAsync(ctx, "Changed")).Symbol.Kind);
    }

    [Fact]
    public async Task Collect_constructor_beside_class_is_ambiguity_listing_both()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public Widget() { }
            }
            """);

        var matches = await CollectAsync(ctx, "Widget");
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Symbol.Kind == SymbolKind.NamedType);
        Assert.Contains(matches, m => m.Symbol.Kind == SymbolKind.Method && ((IMethodSymbol)m.Symbol).MethodKind == MethodKind.Constructor);

        var error = FileDeclarationCollector.FormatAmbiguity("Widget", ctx.FilePath, matches);
        Assert.Contains("Ambiguous", error, StringComparison.Ordinal);
        Assert.Contains("`Demo.Widget`", error, StringComparison.Ordinal);
        var ctorMatch = matches.Single(m => m.Symbol.Kind == SymbolKind.Method);
        var ctorFqn = SymbolDeclarationResolver.GetSymbolFqn(ctorMatch.Symbol);
        Assert.Contains($"`{ctorFqn}`", error, StringComparison.Ordinal);
        Assert.Contains($"{ctorMatch.Line}:{ctorMatch.Column}", error, StringComparison.Ordinal);
        Assert.Contains(":", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Found ", error, StringComparison.Ordinal);
        Assert.NotEqual("Demo.Widget", ctorFqn);
    }

    [Fact]
    public async Task Collect_two_same_named_methods_is_ambiguity_not_first()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public void Run() { }
                public void Run(int x) { }
            }
            """);

        var matches = await CollectAsync(ctx, "Run");
        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal(SymbolKind.Method, m.Symbol.Kind));

        var error = FileDeclarationCollector.FormatAmbiguity("Run", ctx.FilePath, matches);
        Assert.Contains("matches 2 declarations", error, StringComparison.Ordinal);
        Assert.Contains("`Demo.Widget.Run`", error, StringComparison.Ordinal);
        Assert.Equal(2, matches.Count(m => error.Contains($"{m.Line}:{m.Column}", StringComparison.Ordinal)));
        Assert.Contains($"{matches[0].Line}:{matches[0].Column}", error, StringComparison.Ordinal);
        Assert.Contains($"{matches[1].Line}:{matches[1].Column}", error, StringComparison.Ordinal);
        Assert.NotEqual(
            $"{matches[0].Line}:{matches[0].Column}",
            $"{matches[1].Line}:{matches[1].Column}");
    }

    [Fact]
    public async Task Collect_reports_identifier_column_not_declaration_start_with_attributes()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            [Obsolete]
            public class Widget
            {
            }
            """);

        var match = await AssertSingleAsync(ctx, "Widget");

        var root = await ctx.Document.GetSyntaxRootAsync();
        Assert.NotNull(root);
        var classDecl = root!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var identifierColumn = classDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Character + 1;
        var declarationStartColumn = classDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1;

        Assert.Equal(identifierColumn, match.Column);
        Assert.NotEqual(1, match.Column);
        Assert.NotEqual(declarationStartColumn, match.Column);
    }

    [Fact]
    public async Task Collect_finds_struct_and_record()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public struct Point
            {
                public int X;
            }

            public record Person(string Name);
            """);

        Assert.Equal(TypeKind.Struct, ((INamedTypeSymbol)(await AssertSingleAsync(ctx, "Point")).Symbol).TypeKind);
        var person = (INamedTypeSymbol)(await AssertSingleAsync(ctx, "Person")).Symbol;
        Assert.Equal(TypeKind.Class, person.TypeKind);
        Assert.True(person.IsRecord);
    }

    [Fact]
    public async Task Collect_finds_enum_and_event_field()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public enum Color
            {
                Red
            }

            public class Widget
            {
                public event System.EventHandler? Raised;
            }
            """);

        Assert.Equal(TypeKind.Enum, ((INamedTypeSymbol)(await AssertSingleAsync(ctx, "Color")).Symbol).TypeKind);
        Assert.Equal(SymbolKind.Event, (await AssertSingleAsync(ctx, "Raised")).Symbol.Kind);
    }

    [Fact]
    public async Task Collect_file_mode_compare_is_case_sensitive()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
            }
            """);

        Assert.Single(await CollectAsync(ctx, "Widget"));
        Assert.Empty(await CollectAsync(ctx, "widget"));
    }

    [Fact]
    public async Task Collect_zero_matches_formats_not_found_not_reference_list()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
            }
            """);

        var matches = await CollectAsync(ctx, "Missing");
        Assert.Empty(matches);

        var error = FileDeclarationCollector.FormatNotFound("Missing", ctx.FilePath);
        Assert.Contains("was not found", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Found ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("usages", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Collect_does_not_select_operator_or_local_function()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public static Widget operator +(Widget a, Widget b) => a;

                public void Host()
                {
                    void LocalHelper() { }
                }
            }
            """);

        Assert.Empty(await CollectAsync(ctx, "op_Addition"));
        Assert.Empty(await CollectAsync(ctx, "+"));
        Assert.Empty(await CollectAsync(ctx, "LocalHelper"));
    }

    [Fact]
    public async Task Collect_multi_variable_field_matches_only_named_declarator()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public int Alpha, Beta;
            }
            """);

        var alpha = await AssertSingleAsync(ctx, "Alpha");
        Assert.Equal(SymbolKind.Field, alpha.Symbol.Kind);
        Assert.Equal("Alpha", alpha.Symbol.Name);

        var beta = await AssertSingleAsync(ctx, "Beta");
        Assert.Equal("Beta", beta.Symbol.Name);
    }

    [Fact]
    public async Task DefinitionLocationFormatter_prints_both_partial_parts_with_column_and_fqn()
    {
        using var ctx = Fixture.CreatePartial(
            """
            namespace Demo;

            public partial class Widget
            {
                public void A() { }
            }
            """,
            """
            namespace Demo;

            public partial class Widget
            {
                public void B() { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Widget",
            SymbolFilter.Type);
        Assert.Null(error);
        Assert.Single(symbols);

        var symbol = symbols[0];
        var sourceLocations = symbol.Locations
            .Where(l => l.IsInSource && l.SourceTree?.FilePath is not null)
            .ToList();
        Assert.Equal(2, sourceLocations.Count);

        var sb = new StringBuilder();
        foreach (var location in sourceLocations)
        {
            DefinitionLocationFormatter.AppendLocation(sb, symbol, location);
        }

        var text = sb.ToString();
        Assert.Contains("Full name: Demo.Widget", text, StringComparison.Ordinal);
        Assert.Contains("Column:", text, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(text, "File:"));
        Assert.Equal(2, CountOccurrences(text, "Line:"));
        Assert.Equal(2, CountOccurrences(text, "Column:"));
        Assert.Contains(ctx.FilePathA, text, StringComparison.Ordinal);
        Assert.Contains(ctx.FilePathB, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Positional_parameter_resolves_to_parameter_symbol()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public void Run(int value)
                {
                }
            }
            """);

        var (symbol, position, error) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "value",
            line1Based: 5,
            column1Based: null);

        Assert.Null(error);
        Assert.NotNull(position);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Parameter, symbol!.Kind);
        Assert.Equal("value", symbol.Name);
    }

    [Fact]
    public async Task Positional_usage_FindSourceDefinition_returns_source_declaration()
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
        Assert.NotNull(declSymbol);

        var (usageSymbol, _, usageError) = await SourcePositionHelper.ResolveSymbolInDocumentAsync(
            ctx.Document,
            "DoWork",
            line1Based: 9,
            column1Based: null);
        Assert.Null(usageError);
        Assert.NotNull(usageSymbol);

        var sourceDef = await SymbolFinder.FindSourceDefinitionAsync(usageSymbol!, ctx.Solution);
        var resolved = sourceDef ?? usageSymbol!;
        Assert.True(SymbolEqualityComparer.Default.Equals(declSymbol, resolved));
        Assert.True(resolved.Locations.Any(l => l.IsInSource));
    }

    private static async Task<FileDeclarationCollector.Match> AssertSingleAsync(Fixture ctx, string name)
    {
        var matches = await CollectAsync(ctx, name);
        Assert.Single(matches);
        return matches[0];
    }

    private static async Task<IReadOnlyList<FileDeclarationCollector.Match>> CollectAsync(
        Fixture ctx,
        string name)
    {
        var root = await ctx.Document.GetSyntaxRootAsync();
        var model = await ctx.Document.GetSemanticModelAsync();
        Assert.NotNull(root);
        Assert.NotNull(model);
        return FileDeclarationCollector.Collect(root!, model!, name);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(AdhocWorkspace workspace, Document document, string filePath)
        {
            Workspace = workspace;
            Document = document;
            FilePath = filePath;
            Solution = workspace.CurrentSolution;
            FilePathA = filePath;
            FilePathB = filePath;
        }

        private Fixture(
            AdhocWorkspace workspace,
            Solution solution,
            string filePathA,
            string filePathB)
        {
            Workspace = workspace;
            Solution = solution;
            FilePath = filePathA;
            FilePathA = filePathA;
            FilePathB = filePathB;
            Document = solution.Projects.Single().Documents.First();
        }

        public AdhocWorkspace Workspace { get; }
        public Solution Solution { get; }
        public Document Document { get; }
        public string FilePath { get; }
        public string FilePathA { get; }
        public string FilePathB { get; }

        public static Fixture Create(string source)
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            var filePath = Path.Combine(Path.GetTempPath(), "FileDeclSample.cs");
            var document = project.AddDocument("FileDeclSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            var applied = workspace.CurrentSolution.GetDocument(document.Id)
                ?? throw new InvalidOperationException("Document missing after TryApplyChanges.");
            return new Fixture(workspace, applied, filePath);
        }

        public static Fixture CreatePartial(string sourceA, string sourceB)
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            var filePathA = Path.Combine(Path.GetTempPath(), "FileDeclPartialA.cs");
            var filePathB = Path.Combine(Path.GetTempPath(), "FileDeclPartialB.cs");
            var docA = project.AddDocument("FileDeclPartialA.cs", SourceText.From(sourceA), filePath: filePathA);
            project = docA.Project;
            var docB = project.AddDocument("FileDeclPartialB.cs", SourceText.From(sourceB), filePath: filePathB);
            if (!workspace.TryApplyChanges(docB.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for partial test workspace.");
            }

            return new Fixture(workspace, workspace.CurrentSolution, filePathA, filePathB);
        }

        private static Project CreateProject(AdhocWorkspace workspace)
        {
            var info = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "FileDecl.Tests",
                "FileDecl.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "FileDecl.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            return project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));
        }

        public void Dispose() => Workspace.Dispose();
    }
}
