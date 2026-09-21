using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SymbolDeclarationResolverTests
{
    [Fact]
    public async Task Resolve_FQN_method_without_filePath_finds_method()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo.Ns;

            public class Widget
            {
                public void DoWork() { }

                public void Call()
                {
                    DoWork();
                }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Demo.Ns.Widget.DoWork",
            SymbolFilter.Type | SymbolFilter.Member);

        Assert.Null(error);
        Assert.Single(symbols);
        Assert.Equal(SymbolKind.Method, symbols[0].Kind);
        Assert.Equal("DoWork", symbols[0].Name);
        Assert.Equal("Demo.Ns.Widget.DoWork", SymbolDeclarationResolver.GetSymbolFqn(symbols[0]));

        var references = await SymbolFinder.FindReferencesAsync(symbols[0], ctx.Solution);
        Assert.Contains(
            references.SelectMany(r => r.Locations),
            loc => loc.Location.IsInSource);
    }

    [Fact]
    public async Task Simple_name_collision_yields_three_fqn_groups_not_one_primary()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Alpha
            {
                public void Run() { }
            }

            public class Beta
            {
                public void Run() { }
            }

            public class Gamma
            {
                public void Run() { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Run",
            SymbolFilter.Type | SymbolFilter.Member);

        Assert.Null(error);
        Assert.Equal(3, symbols.Count);

        var groups = SymbolDeclarationResolver.GroupByFqn(symbols);
        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.Key == "Demo.Alpha.Run");
        Assert.Contains(groups, g => g.Key == "Demo.Beta.Run");
        Assert.Contains(groups, g => g.Key == "Demo.Gamma.Run");
        Assert.All(groups, g => Assert.Single(g));
    }

    [Fact]
    public async Task Fqn_miss_lists_candidates_and_does_not_resolve_simple_name()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Alpha
            {
                public void Run() { }
            }

            public class Beta
            {
                public void Run() { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Demo.Missing.Run",
            SymbolFilter.Type | SymbolFilter.Member);

        Assert.Empty(symbols);
        Assert.NotNull(error);
        Assert.Contains("No declaration matches FQN `Demo.Missing.Run`", error, StringComparison.Ordinal);
        Assert.Contains("Did not fall back to simple-name search", error, StringComparison.Ordinal);
        Assert.Contains("`Demo.Alpha.Run`", error, StringComparison.Ordinal);
        Assert.Contains("`Demo.Beta.Run`", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Primary symbol", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overloads_share_one_fqn_group()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Calculator
            {
                public void Add() { }
                public void Add(int value) { }
            }
            """);

        var (symbols, error) = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            ctx.Solution,
            "Demo.Calculator.Add",
            SymbolFilter.Type | SymbolFilter.Member);

        Assert.Null(error);
        Assert.Equal(2, symbols.Count);
        Assert.All(symbols, s => Assert.Equal(SymbolKind.Method, s.Kind));

        var groups = SymbolDeclarationResolver.GroupByFqn(symbols);
        Assert.Single(groups);
        Assert.Equal("Demo.Calculator.Add", groups[0].Key);
        Assert.Equal(2, groups[0].Count());
    }

    [Fact]
    public void NormalizeFqn_strips_global_and_empty_parens()
    {
        Assert.Equal("Demo.Widget.DoWork", SymbolDeclarationResolver.NormalizeFqn("global::Demo.Widget.DoWork()"));
        Assert.Equal("Demo.Widget", SymbolDeclarationResolver.NormalizeFqn("  Demo.Widget  "));
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
                "Declaration.Tests",
                "Declaration.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "Declaration.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));

            var filePath = Path.Combine(Path.GetTempPath(), "DeclarationSample.cs");
            var document = project.AddDocument("DeclarationSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            return new Fixture(workspace, workspace.CurrentSolution);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
