using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class ProjectCompilationsTests
{
    [Fact]
    public async Task WalkedSemanticModelIsReleasedAfterTheProject()
    {
        using var workspace = new AdhocWorkspace();
        var id = ProjectId.CreateNewId();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var solution = workspace.CurrentSolution
            .AddProject(id, "Lib", "Lib", LanguageNames.CSharp)
            .WithProjectCompilationOptions(id, options)
            .AddMetadataReference(id, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(id), "Lib.cs", "class Lib { public void M() { } }");
        Assert.True(workspace.TryApplyChanges(solution));

        WeakReference? model = null;
        var binding = new ProjectCompilations(workspace.CurrentSolution);
        await binding.VisitAsync(
            bindBodies: null,
            (project, compilation, cancellationToken) =>
            {
                var semantic = compilation.GetSemanticModel(compilation.SyntaxTrees.First());
                var method = semantic.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                _ = semantic.GetDeclaredSymbol(method, cancellationToken);
                model = new WeakReference(semantic);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var kept = binding.Kept(id);
        Assert.NotNull(kept);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.NotNull(model);
        Assert.False(model.IsAlive);
        Assert.NotNull(kept.GetTypeByMetadataName("Lib"));
    }
}
