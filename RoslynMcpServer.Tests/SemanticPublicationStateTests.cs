using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SemanticPublicationStateTests
{
    [Fact]
    public void ApplyExcludedReferences_removes_only_predetermined_identities()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var nuget = new FakeAnalyzerReference("nuget", @"C:\NuGet\A.dll");
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var other = new FakeAnalyzerReference("other", @"C:\Repro\Other.dll");
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { nuget, original, other }));

        var published = SemanticPublicationState.ApplyExcludedReferences(
            solution,
            [
                new ExcludedAnalyzerReference(projectId, original.FullPath, original.Id),
            ]);

        var refs = published.GetProject(projectId)!.AnalyzerReferences;
        Assert.Equal(2, refs.Count);
        Assert.Same(nuget, refs[0]);
        Assert.Same(other, refs[1]);
    }

    [Fact]
    public void ApplyExcludedReferences_is_noop_when_set_is_empty()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));

        var published = SemanticPublicationState.ApplyExcludedReferences(
            solution,
            Array.Empty<ExcludedAnalyzerReference>());
        Assert.Same(solution, published);
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

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() =>
            ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) =>
            ImmutableArray<DiagnosticAnalyzer>.Empty;
    }
}
