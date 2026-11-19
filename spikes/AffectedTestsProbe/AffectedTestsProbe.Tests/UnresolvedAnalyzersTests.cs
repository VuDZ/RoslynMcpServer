using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class UnresolvedAnalyzersTests
{
    [Fact]
    public void Drop_RemovesOnlyUnresolvedStubs()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(projectId, "Lib", "Lib", LanguageNames.CSharp);
        var project = solution.GetProject(projectId)!;
        var kept = new KeptAnalyzer();
        solution = project
            .AddAnalyzerReference(new UnresolvedAnalyzerReference("missing.analyzer.dll"))
            .AddAnalyzerReference(kept)
            .Solution;

        var dropped = UnresolvedAnalyzers.Drop(solution);

        Assert.NotSame(solution, dropped);
        var references = dropped.GetProject(projectId)!.AnalyzerReferences;
        Assert.DoesNotContain(references, reference => reference is UnresolvedAnalyzerReference);
        Assert.Contains(references, reference => reference == kept);
        Assert.Contains(solution.GetProject(projectId)!.AnalyzerReferences, reference => reference is UnresolvedAnalyzerReference);
    }

    [Fact]
    public void Drop_KeepsTheSameSolutionWhenNothingIsUnresolved()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution.AddProject(ProjectId.CreateNewId(), "Lib", "Lib", LanguageNames.CSharp);

        Assert.Same(solution, UnresolvedAnalyzers.Drop(solution));
    }

    private sealed class KeptAnalyzer : AnalyzerReference
    {
        public override string? FullPath => "kept.dll";

        public override object Id => "kept";

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
    }
}
