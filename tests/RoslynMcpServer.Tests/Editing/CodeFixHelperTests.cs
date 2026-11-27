using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Editing;
using Xunit;

namespace RoslynMcpServer.Tests.Editing;

public sealed class CodeFixHelperTests
{
    [Fact]
    public async Task Built_in_import_fix_is_listed_previewed_and_applied()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("App", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var path = Path.Combine(Path.GetTempPath(), "Example.cs");
        const string source = "public class Example { public List<int> Values = new(); }";
        var document = project.AddDocument("Example.cs", SourceText.From(source), filePath: path);
        Assert.True(workspace.TryApplyChanges(document.Project.Solution));
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        const string diagnosticId = "CS0246";
        const int column = 31;
        var fixes = await CodeFixHelper.GetFixesAsync(document, 1, column, diagnosticId, CancellationToken.None);
        const string import = "using System.Collections.Generic;";
        var fix = Assert.Single(fixes, item => item.Title == import);

        var preview = await CodeFixHelper.BuildPreviewAsync(
            document, 1, column, diagnosticId, fix.FixIndex, CancellationToken.None);
        Assert.Contains(import, preview, StringComparison.Ordinal);
        Assert.Equal(source, (await document.GetTextAsync()).ToString());

        var (solution, changedPaths) = await CodeFixHelper.ApplyFixAsync(
            document, 1, column, diagnosticId, fix.FixIndex, CancellationToken.None);
        Assert.Equal(path, Assert.Single(changedPaths));
        var changed = solution.GetDocument(document.Id)!;
        Assert.Contains(import, (await changed.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var compilation = await changed.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
