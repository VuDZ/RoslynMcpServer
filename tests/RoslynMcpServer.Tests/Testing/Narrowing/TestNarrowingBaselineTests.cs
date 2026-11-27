using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Testing;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots;
using RoslynMcpServer.Tests.SourceStructure;
using RoslynMcpServer.Tests.Testing.Il;
using RoslynMcpServer.Tests.Testing.Snapshots;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>
/// Narrowing through a real snapshot session: the pair must belong to the baseline the epoch 1 estimate was
/// computed against (post-acceptance review of task-03, POST-F-01).
/// </summary>
public sealed class TestNarrowingBaselineTests
{
    [Theory]
    [InlineData("Other.slnx", 0)]
    [InlineData("Solution.slnx", 5)]
    public async Task Pair_of_another_baseline_keeps_every_selected_project_whole(string estimateSolution, int minutesLater)
    {
        // The assemblies and the edit are such that a pair of the right baseline answers "code unchanged". The
        // same output path in another baseline must not give that answer: it would drop every test silently.
        using var fixture = new AssemblySnapshotFixture();
        var (solution, tests) = await PrepareAsync(fixture);
        var session = fixture.Start(new AssemblySnapshotSession(), "Tests");
        var estimate = await EstimateAsync(solution, tests, fixture, Path.Combine(fixture.Root, estimateSolution),
            fixture.PublishedAtUtc.AddMinutes(minutesLater));

        var result = await TestClassNarrower.NarrowAsync(solution, estimate, session, CancellationToken.None);

        var project = Assert.Single(result.Projects);
        Assert.Equal(TestNarrowingOutcome.WholeProject, project.Outcome);
        Assert.Equal(TestNarrowingWholeKind.BaselineChanged, project.Whole!.Kind);
    }

    [Fact]
    public async Task Pair_of_the_estimate_baseline_is_narrowed()
    {
        using var fixture = new AssemblySnapshotFixture();
        var (solution, tests) = await PrepareAsync(fixture);
        var session = fixture.Start(new AssemblySnapshotSession(), "Tests");
        var estimate = await EstimateAsync(solution, tests, fixture, Path.Combine(fixture.Root, "Solution.slnx"),
            fixture.PublishedAtUtc);

        var result = await TestClassNarrower.NarrowAsync(solution, estimate, session, CancellationToken.None);

        Assert.Equal(TestNarrowingOutcome.CodeUnchanged, Assert.Single(result.Projects).Outcome);
    }

    [Fact]
    public async Task Rebuilt_source_also_used_as_an_additional_file_keeps_the_test_project_whole()
    {
        using var fixture = new AssemblySnapshotFixture();
        var (solution, tests) = await PrepareAsync(fixture);
        using var workspace = solution.Workspace;
        var document = Assert.Single(solution.GetProject(tests)!.Documents);
        var originalText = await document.GetTextAsync();
        var path = document.FilePath!;
        var additional = DocumentId.CreateNewId(tests);
        solution = solution.AddAdditionalDocument(additional, document.Name, originalText, filePath: path);
        var solutionPath = Path.Combine(fixture.Root, "Solution.slnx");
        var baseline = new BaselineSession();
        baseline.Publish(solutionPath, solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await Task.WhenAll(baseline.CaptureTask, baseline.Assemblies.FirstReadTask).WaitAsync(AssemblySnapshotFixture.Timeout);

        var source = originalText.ToString() + "\npublic class Helper { }";
        await File.WriteAllTextAsync(path, source);
        File.SetLastWriteTimeUtc(path, fixture.PublishedAtUtc.AddMinutes(1));
        baseline.RecordWrite(path);
        var text = SourceText.From(source);
        solution = solution.WithDocumentText(document.Id, text).WithAdditionalDocumentText(additional, text);
        var rebuilt = IlFixtureCompiler.Compile("Tests", source, Path.GetDirectoryName(fixture.PathOf("Tests"))!);
        File.SetLastWriteTimeUtc(rebuilt, fixture.PublishedAtUtc.AddMinutes(2));

        var inputMap = WorkspaceInputMapBuilder.Build(solution, Guid.NewGuid(), solutionPath, loadGraphComplete: true);
        var estimate = await TestImpactEstimator.EstimateAsync(
            solution, inputMap, baseline.GetSnapshot(), solutionPath, false, false, CancellationToken.None);
        var result = await TestClassNarrower.NarrowAsync(solution, estimate, baseline.Assemblies, CancellationToken.None);

        var project = Assert.Single(result.Projects);
        Assert.Equal(tests, project.ProjectId);
        Assert.Equal(TestNarrowingOutcome.WholeProject, project.Outcome);
        Assert.Equal(TestNarrowingWholeKind.NonCSharpInput, project.Whole?.Kind);
        Assert.Equal(path, project.Whole?.Subject);
    }

    /// <summary>
    /// A test project whose output was built from the current source after its last edit, and whose snapshot
    /// before the edit is the same file: what a session answers for an edit that changed no compiled code.
    /// </summary>
    private static async Task<(Solution Solution, ProjectId Tests)> PrepareAsync(AssemblySnapshotFixture fixture)
    {
        const string Source = "public class LibTests { [Xunit.Fact] public void Runs() { } }";
        var output = IlFixtureCompiler.Compile("Tests", Source, Path.GetDirectoryName(fixture.PathOf("Tests"))!);
        File.SetLastWriteTimeUtc(output, fixture.BeforePublication);
        var document = Path.Combine(fixture.Root, "Tests", "LibTests.cs");
        await File.WriteAllTextAsync(document, Source);
        File.SetLastWriteTimeUtc(document, fixture.BeforePublication.AddHours(-1));

        var workspace = new AdhocWorkspace();
        var tests = ProjectId.CreateNewId("Tests");
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                tests,
                VersionStamp.Create(),
                "Tests",
                "Tests",
                LanguageNames.CSharp,
                filePath: Path.Combine(fixture.Root, "Tests", "Tests.csproj"),
                outputFilePath: output,
                metadataReferences: MetadataReferenceSets.Runtime()))
            .AddDocument(DocumentId.CreateNewId(tests), "LibTests.cs", SourceText.From(Source), filePath: document);
        return (solution, tests);
    }

    /// <summary>Epoch 1 selected the test project whole for a C# edit of its own document.</summary>
    private static async Task<TestImpactEstimate> EstimateAsync(
        Solution solution,
        ProjectId tests,
        AssemblySnapshotFixture fixture,
        string baselineWorkspacePath,
        DateTime baselinePublishedAtUtc)
    {
        var classes = await VstestTestInventory.BuildAsync(solution, CancellationToken.None);
        var reason = new ProjectExpansionReason(ProjectExpansionKind.CSharpSource, tests, Path.Combine(fixture.Root, "Tests", "LibTests.cs"));
        var impact = new TestImpactResult(
            [],
            new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>> { [tests] = [reason] },
            [],
            RequiresReload: false,
            ProjectGraphStale: false);
        return new TestImpactEstimate([], [], impact, [], classes, [], [])
        {
            BaselineWorkspacePath = baselineWorkspacePath,
            BaselinePublishedAtUtc = baselinePublishedAtUtc,
        };
    }
}
