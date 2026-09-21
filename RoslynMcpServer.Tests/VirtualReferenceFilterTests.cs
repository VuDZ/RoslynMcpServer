using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class VirtualReferenceFilterTests
{
    [Fact]
    public async Task Override_directOnly_keeps_own_and_derived_removes_base_and_sibling()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Base
            {
                public virtual void Run() { }
            }

            public class Derived : Base
            {
                public override void Run() { }
            }

            public class Grandchild : Derived
            {
            }

            public class Sibling : Base
            {
            }

            public class Calls
            {
                public void All(Base b, Derived d, Grandchild g, Sibling s)
                {
                    b.Run();
                    d.Run();
                    g.Run();
                    s.Run();
                }
            }
            """);

        var derivedRun = await ctx.GetMethodAsync("Demo.Derived", "Run");
        var refs = await FindSourceReferencesAsync(derivedRun, ctx.Solution);
        Assert.True(refs.Count >= 4, $"expected call sites, got {refs.Count}");

        var filtered = await VirtualReferenceClassifier.ApplyAsync(derivedRun, refs, directOnly: true);
        Assert.Null(filtered.Error);
        Assert.NotNull(filtered.Note);
        Assert.Contains("Kept", filtered.Note, StringComparison.Ordinal);
        Assert.Contains("removed", filtered.Note, StringComparison.OrdinalIgnoreCase);

        var previews = await GetPreviewsAsync(filtered.Locations);
        Assert.Contains(previews, p => p.Contains("d.Run()", StringComparison.Ordinal));
        Assert.Contains(previews, p => p.Contains("g.Run()", StringComparison.Ordinal));
        Assert.DoesNotContain(previews, p => p.Contains("b.Run()", StringComparison.Ordinal));
        Assert.DoesNotContain(previews, p => p.Contains("s.Run()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Base_virtual_directOnly_removes_nothing_and_default_has_no_note()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Base
            {
                public virtual void Run() { }
            }

            public class Derived : Base
            {
            }

            public class Calls
            {
                public void All(Base b, Derived d)
                {
                    b.Run();
                    d.Run();
                }
            }
            """);

        var baseRun = await ctx.GetMethodAsync("Demo.Base", "Run");
        var refs = await FindSourceReferencesAsync(baseRun, ctx.Solution);

        var off = await VirtualReferenceClassifier.ApplyAsync(baseRun, refs, directOnly: false);
        Assert.Null(off.Error);
        Assert.Null(off.Note);
        Assert.Equal(refs.Count, off.Locations.Count);

        var on = await VirtualReferenceClassifier.ApplyAsync(baseRun, refs, directOnly: true);
        Assert.Null(on.Error);
        Assert.Null(on.Note);
        Assert.Equal(refs.Count, on.Locations.Count);
    }

    [Fact]
    public async Task Ordinary_method_directOnly_removes_nothing()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Widget
            {
                public void Run() { }

                public void Call()
                {
                    Run();
                }
            }
            """);

        var run = await ctx.GetMethodAsync("Demo.Widget", "Run");
        Assert.False(VirtualReferenceClassifier.IsApplicableMethod(run));

        var refs = await FindSourceReferencesAsync(run, ctx.Solution);
        var on = await VirtualReferenceClassifier.ApplyAsync(run, refs, directOnly: true);
        Assert.Null(on.Error);
        Assert.Null(on.Note);
        Assert.Equal(refs.Count, on.Locations.Count);
    }

    [Fact]
    public async Task Default_mode_emits_virtual_dispatch_note_when_override_has_base_calls()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Base
            {
                public virtual void Run() { }
            }

            public class Derived : Base
            {
                public override void Run() { }
            }

            public class Calls
            {
                public void All(Base b, Derived d)
                {
                    b.Run();
                    d.Run();
                }
            }
            """);

        var derivedRun = await ctx.GetMethodAsync("Demo.Derived", "Run");
        var refs = await FindSourceReferencesAsync(derivedRun, ctx.Solution);
        var off = await VirtualReferenceClassifier.ApplyAsync(derivedRun, refs, directOnly: false);
        Assert.Null(off.Error);
        Assert.NotNull(off.Note);
        Assert.Contains("virtual dispatch", off.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directOnly: true", off.Note, StringComparison.Ordinal);
        Assert.Equal(refs.Count, off.Locations.Count);
    }

    [Fact]
    public async Task DirectOnly_with_only_virtual_points_returns_zero_remain_error()
    {
        using var ctx = Fixture.Create(
            """
            namespace Demo;

            public class Base
            {
                public virtual void Run() { }
            }

            public class Derived : Base
            {
                public override void Run() { }
            }

            public class Calls
            {
                public void OnlyBase(Base b)
                {
                    b.Run();
                }
            }
            """);

        var derivedRun = await ctx.GetMethodAsync("Demo.Derived", "Run");
        var refs = await FindSourceReferencesAsync(derivedRun, ctx.Solution);
        // Keep only classified call sites that are virtual (exclude declaration Unknown).
        var virtualOnly = new List<ReferenceLocation>();
        foreach (var loc in refs)
        {
            var kind = await VirtualReferenceClassifier.ClassifyAsync(loc, derivedRun.ContainingType!);
            if (kind == VirtualReferenceClassifier.DispatchKind.Virtual)
            {
                virtualOnly.Add(loc);
            }
        }

        Assert.NotEmpty(virtualOnly);
        var result = await VirtualReferenceClassifier.ApplyAsync(derivedRun, virtualOnly, directOnly: true);
        Assert.NotNull(result.Error);
        Assert.Equal(
            VirtualReferenceClassifier.FormatZeroRemainError(virtualOnly.Count),
            result.Error);
        Assert.Contains("directOnly: false", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Locations);
    }

    private static async Task<IReadOnlyList<ReferenceLocation>> FindSourceReferencesAsync(
        ISymbol symbol,
        Solution solution)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
        return references
            .SelectMany(r => r.Locations)
            .Where(l => l.Location.IsInSource && l.Document.FilePath is not null)
            .DistinctBy(l => (
                l.Document.Id,
                l.Location.SourceSpan.Start,
                l.Location.SourceSpan.End))
            .ToList();
    }

    private static async Task<List<string>> GetPreviewsAsync(IReadOnlyList<ReferenceLocation> locations)
    {
        var lines = new List<string>();
        foreach (var location in locations)
        {
            var text = await location.Document.GetTextAsync();
            var line = text.Lines.GetLineFromPosition(location.Location.SourceSpan.Start);
            lines.Add(line.ToString());
        }

        return lines;
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(AdhocWorkspace workspace, Document document, Solution solution)
        {
            Workspace = workspace;
            Document = document;
            Solution = solution;
        }

        public AdhocWorkspace Workspace { get; }
        public Document Document { get; }
        public Solution Solution { get; }

        public static Fixture Create(string source)
        {
            var workspace = new AdhocWorkspace();
            var info = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "VirtualFilter.Tests",
                "VirtualFilter.Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), "VirtualFilter.Tests.csproj"));

            var project = workspace.AddProject(info);
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")));

            var filePath = Path.Combine(Path.GetTempPath(), "VirtualFilterSample.cs");
            var document = project.AddDocument("VirtualFilterSample.cs", SourceText.From(source), filePath: filePath);
            if (!workspace.TryApplyChanges(document.Project.Solution))
            {
                throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
            }

            var applied = workspace.CurrentSolution.GetDocument(document.Id)
                ?? throw new InvalidOperationException("Document missing after TryApplyChanges.");
            return new Fixture(workspace, applied, workspace.CurrentSolution);
        }

        public async Task<IMethodSymbol> GetMethodAsync(string typeMetadataName, string methodName)
        {
            var compilation = await Document.Project.GetCompilationAsync()
                ?? throw new InvalidOperationException("No compilation.");
            var type = compilation.GetTypeByMetadataName(typeMetadataName)
                ?? throw new InvalidOperationException($"Type {typeMetadataName} not found.");
            return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        }

        public void Dispose() => Workspace.Dispose();
    }
}
