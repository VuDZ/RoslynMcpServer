using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class AnalyzerProvenanceBindingTests
{
    [Fact]
    public void Bind_requires_exact_source_output_even_with_one_loaded_candidate()
    {
        using var workspace = new AdhocWorkspace();
        var fixture = CreateFixture(workspace, analyzerIdentityMatchesOutput: false, effectiveTargetFramework: "netstandard2.0");

        var binding = AnalyzerProvenanceCaptureService.Bind(
            fixture.Solution,
            fixture.Contexts,
            fixture.Item);

        Assert.Equal(AnalyzerProvenanceBindingStatus.AmbiguousSourceProject, binding.Status);
        Assert.Null(binding.SourceProjectId);
    }

    [Fact]
    public void Bind_rejects_target_framework_conflict_after_exact_output_hit()
    {
        using var workspace = new AdhocWorkspace();
        var fixture = CreateFixture(workspace, analyzerIdentityMatchesOutput: true, effectiveTargetFramework: "net10.0");

        var binding = AnalyzerProvenanceCaptureService.Bind(
            fixture.Solution,
            fixture.Contexts,
            fixture.Item);

        Assert.Equal(AnalyzerProvenanceBindingStatus.AmbiguousSourceProject, binding.Status);
        Assert.Null(binding.SourceProjectId);
    }

    [Fact]
    public void Bind_confirms_one_exact_output_with_matching_effective_target_framework()
    {
        using var workspace = new AdhocWorkspace();
        var fixture = CreateFixture(workspace, analyzerIdentityMatchesOutput: true, effectiveTargetFramework: "netstandard2.0");

        var binding = AnalyzerProvenanceCaptureService.Bind(
            fixture.Solution,
            fixture.Contexts,
            fixture.Item);

        Assert.Equal(AnalyzerProvenanceBindingStatus.Confirmed, binding.Status);
        Assert.Equal(fixture.SourceProjectId, binding.SourceProjectId);
        Assert.Equal("netstandard2.0", binding.SelectedInnerTargetFramework);
    }

    private static BindingFixture CreateFixture(
        AdhocWorkspace workspace,
        bool analyzerIdentityMatchesOutput,
        string effectiveTargetFramework)
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.BindingTests", Guid.NewGuid().ToString("N"));
        var consumerProjectFile = Path.Combine(root, "Consumer", "Consumer.csproj");
        var sourceProjectFile = Path.Combine(root, "Generator", "Generator.csproj");
        var sourceOutput = Path.Combine(root, "Generator", "bin", "Debug", "netstandard2.0", "Generator.dll");
        var analyzerIdentity = analyzerIdentityMatchesOutput
            ? sourceOutput
            : Path.Combine(root, "stale", "Generator.dll");
        var consumerId = ProjectId.CreateNewId("Consumer");
        var sourceId = ProjectId.CreateNewId("Generator");
        var loader = new InProcessAnalyzerAssemblyLoader();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                    sourceId,
                    VersionStamp.Create(),
                    "Generator",
                    "Generator",
                    LanguageNames.CSharp,
                    filePath: sourceProjectFile,
                    outputFilePath: sourceOutput)
                .WithCompilationOutputInfo(new CompilationOutputInfo().WithAssemblyPath(sourceOutput)))
            .AddProject(ProjectInfo.Create(
                    consumerId,
                    VersionStamp.Create(),
                    "Consumer",
                    "Consumer",
                    LanguageNames.CSharp,
                    filePath: consumerProjectFile)
                .WithAnalyzerReferences([new AnalyzerFileReference(analyzerIdentity, loader)]));
        var consumerContext = new AnalyzerProvenanceProjectContextKey(1, 1, 1, 1);
        var taskContext = new AnalyzerProvenanceEventContextKey(1, 1, 1, 1, 1, 1);
        var sourceContext = new AnalyzerProvenanceProjectContextKey(1, 1, 2, 2);
        var contexts = ImmutableArray.Create(
            new AnalyzerProvenanceProjectContext(
                0,
                consumerContext,
                null,
                consumerProjectFile,
                ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<string, string>.Empty),
            new AnalyzerProvenanceProjectContext(
                0,
                sourceContext,
                taskContext,
                sourceProjectFile,
                ImmutableDictionary<string, string>.Empty.Add("TargetFramework", effectiveTargetFramework),
                ImmutableDictionary<string, string>.Empty));
        var item = new CapturedAnalyzerProvenanceItem(
            0,
            consumerContext,
            taskContext,
            analyzerIdentity,
            sourceProjectFile,
            ReferenceSourceTarget: null,
            OutputItemType: "Analyzer",
            ReferenceOutputAssembly: "false",
            NearestTargetFramework: "netstandard2.0",
            SetTargetFramework: null,
            SetConfiguration: null,
            SetPlatform: null,
            GlobalPropertiesToRemove: null,
            UndefineProperties: null,
            ImmutableDictionary<string, string>.Empty);
        return new BindingFixture(solution, contexts, item, sourceId);
    }

    private sealed record BindingFixture(
        Solution Solution,
        ImmutableArray<AnalyzerProvenanceProjectContext> Contexts,
        CapturedAnalyzerProvenanceItem Item,
        ProjectId SourceProjectId);
}
