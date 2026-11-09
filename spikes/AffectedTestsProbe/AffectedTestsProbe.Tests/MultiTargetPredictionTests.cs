using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class MultiTargetPredictionTests
{
    [Fact]
    public async Task SecondFramework_IsIncludedWhenTestsDoNotReferenceTheFirst()
    {
        var libPath = Path.Combine(Path.GetTempPath(), "tia-lib-" + Guid.NewGuid().ToString("N") + ".cs");
        var testPath = Path.Combine(Path.GetTempPath(), "tia-tests-" + Guid.NewGuid().ToString("N") + ".cs");
        const string library =
            """
            public class Lib
            {
                public int Add(int a, int b) => a + b;
            }
            """;
        const string tests =
            """
            using Xunit;
            public class SampleTests
            {
                [Fact]
                public void Adds()
                {
                    _ = new Lib().Add(1, 2);
                }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var standardId = ProjectId.CreateNewId();
        var modernId = ProjectId.CreateNewId();
        var testId = ProjectId.CreateNewId();
        var references = ProbeSession.MetadataReferences();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var solution = workspace.CurrentSolution
            .AddProject(standardId, "Lib (netstandard2.0)", "Lib", LanguageNames.CSharp)
            .WithProjectCompilationOptions(standardId, options)
            .WithProjectParseOptions(standardId, parse)
            .AddMetadataReferences(standardId, references)
            .AddProject(modernId, "Lib (net10.0)", "Lib", LanguageNames.CSharp)
            .WithProjectCompilationOptions(modernId, options)
            .WithProjectParseOptions(modernId, parse)
            .AddMetadataReferences(modernId, references)
            .AddProject(testId, "SampleTests (net10.0)", "SampleTests", LanguageNames.CSharp)
            .WithProjectCompilationOptions(testId, options)
            .WithProjectParseOptions(testId, parse)
            .AddMetadataReferences(testId, references)
            .AddProjectReference(testId, new ProjectReference(modernId))
            .AddDocument(DocumentId.CreateNewId(standardId), "Lib.cs", SourceText.From(library), filePath: libPath)
            .AddDocument(DocumentId.CreateNewId(modernId), "Lib.cs", SourceText.From(library), filePath: libPath)
            .AddDocument(DocumentId.CreateNewId(testId), "Tests.cs", SourceText.From(tests), filePath: testPath);
        Assert.True(workspace.TryApplyChanges(solution));
        solution = workspace.CurrentSolution;

        var map = new ProjectMap(solution, "Debug", "AnyCPU");
        var roots = await RootIndexBuilder.BuildAsync(solution, map, ["SampleTests"], CancellationToken.None);
        var facts = await FactExtractor.ExtractAsync(solution, map, CancellationToken.None);
        var reach = ForwardReachability.Compute(facts, roots, map);
        var modern = solution.GetProject(modernId)!;
        var compilation = await modern.GetCompilationAsync();
        var method = compilation!.GetTypeByMetadataName("Lib")!.GetMembers("Add").OfType<IMethodSymbol>().Single();
        var id = method.GetDocumentationCommentId()!;
        var locations = await MemberLocations.FindAsync(solution, map, id, projectPath: null, CancellationToken.None);

        Assert.Equal(2, locations.Count);
        var first = locations.Single(location => location.Node.Project.TargetFramework == "netstandard2.0");
        var second = locations.Single(location => location.Node.Project.TargetFramework == "net10.0");
        Assert.Empty(reach.Units(DispatchKind.Cha, first.Node));
        Assert.Contains(reach.Units(DispatchKind.Cha, second.Node), unit => unit.MethodName == "Adds");
        Assert.Contains(
            MemberLocations.ForwardKeys(reach, DispatchKind.Cha, locations),
            key => key.Contains(".Adds", StringComparison.Ordinal));

        var reverse = await ReverseSearch.SearchAsync(solution, map, roots, second.Node, ProbeLimits.Default, CancellationToken.None);
        Assert.Contains(reverse.Units, unit => unit.MethodName == "Adds");
    }
}
