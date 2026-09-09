using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public class AnalyzerReferenceShadowCopierTests
{
    [Fact]
    public void ShadowCopyInSolutionAnalyzerReferences_rewrites_reference_matching_in_solution_project_assembly_name()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        // The reference recorded on Consumer intentionally points at a *different*, non-existent path —
        // this is the observed MSBuildWorkspace behavior when Directory.Build.props overrides OutputPath.
        var brokenReferencePath = Path.Combine(fixture.RootDirectory, "artifacts", "Generator", "Generator.dll");

        var solution = fixture.BuildSolution(
            generatorOutputFilePath: generatorOutput,
            consumerAnalyzerReferences: new AnalyzerReference[] { new FakeAnalyzerReference("Unresolved: " + brokenReferencePath, brokenReferencePath) });

        var loader = new InProcessAnalyzerAssemblyLoader();
        var (newSolution, results) = AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences(
            solution, fixture.ShadowRoot, loader);

        var result = Assert.Single(results);
        Assert.True(result.Applied);
        Assert.Equal("Consumer", result.ProjectName);
        Assert.Equal("Generator", result.MatchedProjectName);
        Assert.Equal(brokenReferencePath, result.OriginalFullPath);
        Assert.NotNull(result.ShadowCopyPath);
        Assert.True(File.Exists(result.ShadowCopyPath));
        Assert.StartsWith(fixture.ShadowRoot, result.ShadowCopyPath, StringComparison.OrdinalIgnoreCase);

        var consumer = newSolution.Projects.Single(p => p.Name == "Consumer");
        var rewritten = Assert.Single(consumer.AnalyzerReferences);
        Assert.Equal(result.ShadowCopyPath, rewritten.FullPath);
        Assert.IsType<AnalyzerFileReference>(rewritten);
    }

    [Fact]
    public void ShadowCopyInSolutionAnalyzerReferences_skips_when_matched_project_has_no_output_on_disk()
    {
        using var fixture = new ReproFixture();
        var missingGeneratorOutput = Path.Combine(fixture.RootDirectory, "Generator", "obj", "Debug", "Generator.dll");
        var brokenReferencePath = Path.Combine(fixture.RootDirectory, "artifacts", "Generator", "Generator.dll");

        var solution = fixture.BuildSolution(
            generatorOutputFilePath: missingGeneratorOutput, // never created on disk
            consumerAnalyzerReferences: new AnalyzerReference[] { new FakeAnalyzerReference("Unresolved: " + brokenReferencePath, brokenReferencePath) });

        var loader = new InProcessAnalyzerAssemblyLoader();
        var (newSolution, results) = AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences(
            solution, fixture.ShadowRoot, loader);

        var result = Assert.Single(results);
        Assert.False(result.Applied);
        Assert.NotNull(result.SkipReason);

        // Unchanged: same reference instance still present, nothing rewritten.
        var consumer = newSolution.Projects.Single(p => p.Name == "Consumer");
        Assert.Same(solution.Projects.Single(p => p.Name == "Consumer").AnalyzerReferences.Single(), consumer.AnalyzerReferences.Single());
    }

    [Fact]
    public void ShadowCopyInSolutionAnalyzerReferences_ignores_references_not_matching_any_in_solution_project()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        var nugetAnalyzerPath = fixture.CreateFile("nuget-cache", "SomeAnalyzers.dll");

        var solution = fixture.BuildSolution(
            generatorOutputFilePath: generatorOutput,
            consumerAnalyzerReferences: new AnalyzerReference[] { new FakeAnalyzerReference("SomeAnalyzers", nugetAnalyzerPath) });

        var loader = new InProcessAnalyzerAssemblyLoader();
        var (newSolution, results) = AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences(
            solution, fixture.ShadowRoot, loader);

        Assert.Empty(results);
        var consumer = newSolution.Projects.Single(p => p.Name == "Consumer");
        Assert.Same(nugetAnalyzerPath, consumer.AnalyzerReferences.Single().FullPath);
    }

    [Fact]
    public void GetDefaultShadowRootDirectory_is_stable_for_same_path_and_distinct_for_different_paths()
    {
        var a1 = AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(@"C:\Repro\GenRepro.slnx");
        var a2 = AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(@"C:\Repro\GenRepro.slnx");
        var b = AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(@"C:\Other\GenRepro.slnx");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    private sealed class FakeAnalyzerReference : AnalyzerReference
    {
        public FakeAnalyzerReference(string display, string? fullPath)
        {
            Display = display;
            FullPath = fullPath;
        }

        public override string Display { get; }

        public override string? FullPath { get; }

        public override object Id => Display;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => ImmutableArray<DiagnosticAnalyzer>.Empty;
    }

    /// <summary>Builds a two-project (Consumer/Generator) <see cref="AdhocWorkspace"/> fixture with a temp directory.</summary>
    private sealed class ReproFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();

        public ReproFixture()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            ShadowRoot = Path.Combine(RootDirectory, "shadow");
        }

        public string RootDirectory { get; }

        public string ShadowRoot { get; }

        public string CreateFile(params string[] relativeSegments)
        {
            var path = Path.Combine(RootDirectory, Path.Combine(relativeSegments));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 0 });
            return path;
        }

        public Solution BuildSolution(string generatorOutputFilePath, IReadOnlyList<AnalyzerReference> consumerAnalyzerReferences)
        {
            var solution = _workspace.CurrentSolution;

            var generatorInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(debugName: "Generator"),
                    VersionStamp.Create(),
                    "Generator",
                    "Generator",
                    LanguageNames.CSharp,
                    filePath: null,
                    outputFilePath: generatorOutputFilePath)
                .WithCompilationOutputInfo(new CompilationOutputInfo().WithAssemblyPath(generatorOutputFilePath));
            solution = solution.AddProject(generatorInfo);

            var consumerInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(debugName: "Consumer"),
                    VersionStamp.Create(),
                    "Consumer",
                    "Consumer",
                    LanguageNames.CSharp,
                    filePath: null,
                    outputFilePath: CreateFile("Consumer", "obj", "Debug", "Consumer.dll"))
                .WithAnalyzerReferences(consumerAnalyzerReferences);
            solution = solution.AddProject(consumerInfo);

            return solution;
        }

        public void Dispose()
        {
            _workspace.Dispose();
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
