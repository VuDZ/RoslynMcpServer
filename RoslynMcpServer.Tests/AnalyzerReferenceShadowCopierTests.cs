using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public class AnalyzerReferenceShadowCopierTests
{
    [Fact]
    public void PrepareInSolutionAnalyzerReferences_rewrites_only_provenance_confirmed_reference()
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
        var sessionId = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            sessionId,
            solution.Projects.Single(p => p.Name == "Consumer").Id,
            solution.Projects.Single(p => p.Name == "Generator").Id,
            brokenReferencePath);
        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            loader,
            previousMapping: null,
            sessionId,
            loadedPath: Path.Combine(fixture.RootDirectory, "repro.sln"),
            snapshot);
        var newSolution = prepared.Mapping.Apply(solution, loader);
        var results = prepared.Results;

        var result = Assert.Single(results);
        Assert.True(result.Applied);
        Assert.Equal("Consumer", result.ProjectName);
        Assert.Equal("Generator", result.MatchedProjectName);
        Assert.Equal(brokenReferencePath, result.OriginalFullPath);
        Assert.NotNull(result.ShadowCopyPath);
        Assert.True(File.Exists(result.ShadowCopyPath));
        Assert.StartsWith(fixture.ShadowRoot, result.ShadowCopyPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v2-main-only", result.ShadowCopyPath, StringComparison.OrdinalIgnoreCase);

        var consumer = newSolution.Projects.Single(p => p.Name == "Consumer");
        var rewritten = Assert.Single(consumer.AnalyzerReferences);
        Assert.Equal(result.ShadowCopyPath, rewritten.FullPath);
        Assert.IsType<AnalyzerFileReference>(rewritten);
    }

    [Fact]
    public void PrepareInSolutionAnalyzerReferences_skips_when_confirmed_project_has_no_output_on_disk()
    {
        using var fixture = new ReproFixture();
        var missingGeneratorOutput = Path.Combine(fixture.RootDirectory, "Generator", "obj", "Debug", "Generator.dll");
        var brokenReferencePath = Path.Combine(fixture.RootDirectory, "artifacts", "Generator", "Generator.dll");

        var solution = fixture.BuildSolution(
            generatorOutputFilePath: missingGeneratorOutput, // never created on disk
            consumerAnalyzerReferences: new AnalyzerReference[] { new FakeAnalyzerReference("Unresolved: " + brokenReferencePath, brokenReferencePath) });

        var loader = new InProcessAnalyzerAssemblyLoader();
        var sessionId = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            sessionId,
            solution.Projects.Single(p => p.Name == "Consumer").Id,
            solution.Projects.Single(p => p.Name == "Generator").Id,
            brokenReferencePath);
        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            loader,
            previousMapping: null,
            sessionId,
            loadedPath: Path.Combine(fixture.RootDirectory, "repro.sln"),
            snapshot);
        var newSolution = prepared.Mapping.Apply(solution, loader);
        var results = prepared.Results;

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
    public void ShadowCopyInSolutionAnalyzerReferences_does_not_fallback_to_same_assembly_name_without_provenance()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        var sameNameReference = fixture.CreateFile("external", "Generator.dll");
        var solution = fixture.BuildSolution(
            generatorOutput,
            new AnalyzerReference[] { new FakeAnalyzerReference("external", sameNameReference) });

        var loader = new InProcessAnalyzerAssemblyLoader();
        var (unchanged, results) = AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            loader);

        Assert.Empty(results);
        Assert.Same(
            solution.Projects.Single(p => p.Name == "Consumer").AnalyzerReferences.Single(),
            unchanged.Projects.Single(p => p.Name == "Consumer").AnalyzerReferences.Single());
    }

    [Fact]
    public void PrepareInSolutionAnalyzerReferences_preserves_same_name_foreign_reference()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        var producedReferencePath = Path.Combine(fixture.RootDirectory, "stale", "Generator.dll");
        var foreignReferencePath = fixture.CreateFile("external", "Generator.dll");
        var solution = fixture.BuildSolution(
            generatorOutput,
            new AnalyzerReference[]
            {
                new FakeAnalyzerReference("produced", producedReferencePath),
                new FakeAnalyzerReference("foreign", foreignReferencePath),
            });
        var consumer = solution.Projects.Single(p => p.Name == "Consumer");
        var generator = solution.Projects.Single(p => p.Name == "Generator");
        var sessionId = Guid.NewGuid();
        var snapshot = CreateSnapshot(sessionId, consumer.Id, generator.Id, producedReferencePath);
        var loader = new InProcessAnalyzerAssemblyLoader();

        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            loader,
            previousMapping: null,
            sessionId,
            loadedPath: Path.Combine(fixture.RootDirectory, "repro.sln"),
            snapshot);
        var rewritten = prepared.Mapping.Apply(solution, loader);
        var paths = rewritten.GetProject(consumer.Id)!.AnalyzerReferences.Select(reference => reference.FullPath).ToArray();

        Assert.Single(prepared.Results);
        Assert.Contains(paths, path => path is not null && AnalyzerShadowMapping.PathsEqual(path, foreignReferencePath));
        Assert.Contains(paths, path => path is not null && path.StartsWith(fixture.ShadowRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrepareInSolutionAnalyzerReferences_skips_ambiguous_confirmed_sources()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        var originalPath = Path.Combine(fixture.RootDirectory, "stale", "Generator.dll");
        var solution = fixture.BuildSolution(
            generatorOutput,
            new AnalyzerReference[] { new FakeAnalyzerReference("produced", originalPath) });
        var consumer = solution.Projects.Single(p => p.Name == "Consumer");
        var generator = solution.Projects.Single(p => p.Name == "Generator");
        var otherGenerator = ProjectId.CreateNewId("OtherGenerator");
        solution = solution.AddProject(ProjectInfo.Create(
            otherGenerator,
            VersionStamp.Create(),
            "OtherGenerator",
            "Generator",
            LanguageNames.CSharp,
            outputFilePath: fixture.CreateFile("OtherGenerator", "Generator.dll")));
        var sessionId = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            sessionId,
            consumer.Id,
            generator.Id,
            originalPath,
            new AnalyzerProvenanceBinding(
                AnalyzerProvenanceBindingStatus.Confirmed,
                consumer.Id,
                otherGenerator,
                originalPath,
                SourceProjectFile: null,
                SelectedInnerTargetFramework: null,
                ImmutableDictionary<string, string>.Empty));

        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            new InProcessAnalyzerAssemblyLoader(),
            previousMapping: null,
            sessionId,
            loadedPath: Path.Combine(fixture.RootDirectory, "repro.sln"),
            snapshot);

        Assert.Empty(prepared.Results);
        Assert.False(prepared.Mapping.HasAnyApplied);
    }

    [Fact]
    public void ApplyMapping_does_not_read_analyzer_files_and_survives_missing_source()
    {
        using var fixture = new ReproFixture();
        var generatorOutput = fixture.CreateFile("Generator", "obj", "Debug", "Generator.dll");
        var brokenReferencePath = Path.Combine(fixture.RootDirectory, "artifacts", "Generator", "Generator.dll");
        var solution = fixture.BuildSolution(
            generatorOutputFilePath: generatorOutput,
            consumerAnalyzerReferences: new AnalyzerReference[] { new FakeAnalyzerReference("Unresolved: " + brokenReferencePath, brokenReferencePath) });
        var loader = new InProcessAnalyzerAssemblyLoader();
        var sessionId = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            sessionId,
            solution.Projects.Single(p => p.Name == "Consumer").Id,
            solution.Projects.Single(p => p.Name == "Generator").Id,
            brokenReferencePath);

        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            solution,
            fixture.ShadowRoot,
            loader,
            previousMapping: null,
            sessionId,
            loadedPath: Path.Combine(fixture.RootDirectory, "repro.slnx"),
            snapshot);
        Assert.True(prepared.Mapping.HasAnyApplied);
        File.Delete(generatorOutput);
        var ioBefore = AnalyzerShadowGenerationPublisher.AnalyzerFileIoCount;

        var reapplied = prepared.Mapping.Apply(solution, loader);
        Assert.Equal(ioBefore, AnalyzerShadowGenerationPublisher.AnalyzerFileIoCount);
        var rewritten = Assert.Single(reapplied.Projects.Single(p => p.Name == "Consumer").AnalyzerReferences);
        Assert.Equal(prepared.Mapping.Entries[0].ShadowCopyPath, rewritten.FullPath);
        Assert.True(File.Exists(rewritten.FullPath));
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

    private static AnalyzerProvenanceSnapshot CreateSnapshot(
        Guid sessionId,
        ProjectId consumerProjectId,
        ProjectId sourceProjectId,
        string identity,
        params AnalyzerProvenanceBinding[] additionalBindings)
    {
        var confirmed = new AnalyzerProvenanceBinding(
            AnalyzerProvenanceBindingStatus.Confirmed,
            consumerProjectId,
            sourceProjectId,
            identity,
            SourceProjectFile: null,
            SelectedInnerTargetFramework: null,
            ImmutableDictionary<string, string>.Empty);
        var bindings = new[] { confirmed }.Concat(additionalBindings).ToImmutableArray();
        return new AnalyzerProvenanceSnapshot(
            sessionId,
            LoadedPath: "repro.sln",
            ImmutableDictionary<string, string>.Empty,
            new AnalyzerProvenanceToolset(null, null, "", null, null, null),
            AnalyzerProvenanceCaptureStatus.Complete,
            [],
            [],
            bindings,
            [],
            new AnalyzerProvenanceCaptureMetrics(1, 0, TimeSpan.Zero, 0, bindings.Length, bindings.Length));
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
