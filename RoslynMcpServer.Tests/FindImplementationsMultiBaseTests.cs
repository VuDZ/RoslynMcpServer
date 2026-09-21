using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class FindImplementationsMultiBaseTests
{
    [Fact]
    public async Task Exact_FQN_selects_one_base_type_when_simple_names_collide()
    {
        using var ctx = Fixture.Create(
            """
            namespace Alpha
            {
                public interface IHandler { }
            }

            namespace Beta
            {
                public interface IHandler { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Alpha.IHandler",
            SymbolFilter.Type);

        Assert.Null(error);
        Assert.Single(symbols);
        Assert.Equal("Alpha.IHandler", SymbolDeclarationResolver.GetSymbolFqn(symbols[0]));
        Assert.DoesNotContain(symbols, s => SymbolDeclarationResolver.GetSymbolFqn(s) == "Beta.IHandler");
    }

    [Fact]
    public async Task Fqn_miss_lists_candidates_and_does_not_resolve_simple_name()
    {
        using var ctx = Fixture.Create(
            """
            namespace Alpha
            {
                public interface IHandler { }
            }

            namespace Beta
            {
                public interface IHandler { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Missing.IHandler",
            SymbolFilter.Type);

        Assert.Empty(symbols);
        Assert.NotNull(error);
        Assert.Contains("No declaration matches FQN `Missing.IHandler`", error, StringComparison.Ordinal);
        Assert.Contains("Did not fall back to simple-name search", error, StringComparison.Ordinal);
        Assert.Contains("`Alpha.IHandler`", error, StringComparison.Ordinal);
        Assert.Contains("`Beta.IHandler`", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Name_without_global_prefix_is_found()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo.Ns
            {
                public interface IWidget { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Demo.Ns.IWidget",
            SymbolFilter.Type);

        Assert.Null(error);
        Assert.Single(symbols);
        Assert.Equal("Demo.Ns.IWidget", SymbolDeclarationResolver.GetSymbolFqn(symbols[0]));

        var (withGlobal, globalError) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "global::Demo.Ns.IWidget",
            SymbolFilter.Type);

        Assert.Null(globalError);
        Assert.Single(withGlobal);
        Assert.Equal("Demo.Ns.IWidget", SymbolDeclarationResolver.GetSymbolFqn(withGlobal[0]));
    }

    [Fact]
    public async Task Two_IHandler_bases_each_include_their_implementations()
    {
        using var ctx = Fixture.Create(
            """
            namespace Alpha
            {
                public interface IHandler { }
                public class AlphaHandler : IHandler { }
            }

            namespace Beta
            {
                public interface IHandler { }
                public class BetaHandler : IHandler { }
            }
            """);

        var text = await FormatFindImplementationsAsync(ctx.Solution, "IHandler", transitive: true);

        Assert.Contains("## implementations for `IHandler`", text, StringComparison.Ordinal);
        Assert.Contains("### `Alpha.IHandler`", text, StringComparison.Ordinal);
        Assert.Contains("### `Beta.IHandler`", text, StringComparison.Ordinal);
        Assert.Contains("`AlphaHandler`", text, StringComparison.Ordinal);
        Assert.Contains("`BetaHandler`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Other candidates", text, StringComparison.Ordinal);
        Assert.DoesNotContain("primary symbol", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unique_interface_keeps_single_type_header_without_other_candidates_warning()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo
            {
                public interface IUnique { }
                public class UniqueImpl : IUnique { }
            }
            """);

        var text = await FormatFindImplementationsAsync(ctx.Solution, "IUnique", transitive: true);

        Assert.Contains("## implementations (transitive) for `IUnique`", text, StringComparison.Ordinal);
        Assert.Contains("**Base symbol:**", text, StringComparison.Ordinal);
        Assert.Contains("`UniqueImpl`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("## implementations for `IUnique`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("matching base type(s)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Other candidates", text, StringComparison.Ordinal);
        Assert.DoesNotContain("### `", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_implementation_section_is_written_explicitly_for_one_of_two_bases()
    {
        using var ctx = Fixture.Create(
            """
            namespace Alpha
            {
                public interface IHandler { }
                public class AlphaHandler : IHandler { }
            }

            namespace Beta
            {
                public interface IHandler { }
            }
            """);

        var text = await FormatFindImplementationsAsync(ctx.Solution, "IHandler", transitive: true);

        Assert.Contains("### `Alpha.IHandler`", text, StringComparison.Ordinal);
        Assert.Contains("### `Beta.IHandler`", text, StringComparison.Ordinal);
        Assert.Contains("`AlphaHandler`", text, StringComparison.Ordinal);
        Assert.Contains("(no implementations)", text, StringComparison.Ordinal);
    }

    private static async Task<string> FormatFindImplementationsAsync(
        Solution solution,
        string symbolName,
        bool transitive)
    {
        var (declarations, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            solution,
            symbolName,
            SymbolFilter.Type);
        Assert.Null(error);

        var typeSymbols = declarations
            .OfType<INamedTypeSymbol>()
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .Where(t => t.TypeKind is TypeKind.Interface or TypeKind.Class or TypeKind.Struct)
            .OrderBy(t => SymbolDeclarationResolver.GetSymbolFqn(t), StringComparer.Ordinal)
            .ToList();

        var sections = new List<ImplementationListingFormatter.BaseSection>();
        foreach (var type in typeSymbols)
        {
            IEnumerable<INamedTypeSymbol> related = type.TypeKind switch
            {
                TypeKind.Interface => await SymbolFinder.FindImplementationsAsync(
                    type, solution, transitive, projects: null, CancellationToken.None),
                TypeKind.Class or TypeKind.Struct => await SymbolFinder.FindDerivedClassesAsync(
                    type, solution, transitive, projects: null, CancellationToken.None),
                _ => []
            };

            var relatedLines = related
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>()
                .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .Select(t => ImplementationListingFormatter.ToRelatedTypeLine(t))
                .ToList();

            sections.Add(
                new ImplementationListingFormatter.BaseSection(
                    SymbolDeclarationResolver.GetSymbolFqn(type),
                    ImplementationListingFormatter.GetTypeKindLabel(type),
                    ImplementationListingFormatter.GetSearchMode(type.TypeKind, transitive),
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    relatedLines));
        }

        var sb = new StringBuilder();
        if (sections.Count == 1)
        {
            var section = sections[0];
            ImplementationListingFormatter.AppendSingleTypeHeader(sb, symbolName, section);
            if (section.RelatedTypes.Count == 0)
            {
                sb.AppendLine($"No {section.SearchMode} were found in the solution.");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"Found **{section.RelatedTypes.Count}** type(s):");
            sb.AppendLine();
            foreach (var line in ImplementationListingFormatter.BuildNumberedRelatedLines(section.RelatedTypes))
            {
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        ImplementationListingFormatter.AppendMultiTypeHeader(
            sb,
            symbolName,
            sections.Count,
            sections.Sum(s => s.RelatedTypes.Count));
        foreach (var line in ImplementationListingFormatter.BuildMultiSectionLines(sections))
        {
            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(AdhocWorkspace workspace, Solution solution)
        {
            Workspace = workspace;
            Solution = solution;
        }

        public AdhocWorkspace Workspace { get; }
        public Solution Solution { get; }

        public static Fixture Create(string source)
        {
            var workspace = new AdhocWorkspace();
            var info = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "Implementations.Tests",
                "Implementations.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "Implementations.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));

            var filePath = Path.Combine(Path.GetTempPath(), "ImplementationsSample.cs");
            var document = project.AddDocument("ImplementationsSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            return new Fixture(workspace, workspace.CurrentSolution);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
