using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// Regression coverage for the analyzer-reference shadow-copy overlay guard in <see cref="SolutionManager"/>.
/// See <c>docs/ARCHITECTURE.md</c> and the v1.3.5 README entry: applying the overlay via
/// <see cref="Workspace.TryApplyChanges(Solution)"/> against the real <c>MSBuildWorkspace</c> persisted the
/// analyzer reference change into the backing <c>.csproj</c> (confirmed against the <c>C:\Scratch\GenRepro</c>
/// repro), corrupting it with a machine-specific temp path and leaving a duplicate generator reference. These
/// tests exercise the private guard directly (no live <c>MSBuildWorkspace</c> needed — it only transforms plain
/// <see cref="Solution"/> objects) since a full <c>MSBuildWorkspace</c> integration test would need MSBuild
/// bootstrap not currently wired into this test project.
/// </summary>
public sealed class SolutionManagerAnalyzerOverlayTests
{
    [Fact]
    public void RevertAnalyzerReferenceOverlayForApply_strips_overlay_diff_when_shadow_copy_enabled()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var originalReference = new FakeAnalyzerReference("original", @"C:\Repro\Generator\obj\Debug\Generator.dll");
        var shadowReference = new FakeAnalyzerReference("shadow", @"C:\Temp\RoslynMcpServer.AnalyzerShadowCopy\Generator.dll");

        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
            .WithAnalyzerReferences(new AnalyzerReference[] { originalReference });
        var workspaceCurrentSolution = workspace.CurrentSolution.AddProject(projectInfo);

        // Mirrors what GetCurrentSolution() hands back once ShadowCopyInSolutionAnalyzerReferencesAsync ran:
        // same project, but the AnalyzerReference rewritten to the shadow copy.
        var candidateSolution = workspaceCurrentSolution.WithProjectAnalyzerReferences(
            projectId,
            new AnalyzerReference[] { shadowReference });

        var manager = CreateManagerWithShadowCopyFlag(enabled: true);
        var reverted = InvokeRevertGuard(manager, candidateSolution, workspaceCurrentSolution);

        var revertedProject = reverted.GetProject(projectId)!;
        Assert.Same(originalReference, revertedProject.AnalyzerReferences.Single());
    }

    [Fact]
    public void RevertAnalyzerReferenceOverlayForApply_is_noop_when_shadow_copy_not_enabled()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var originalReference = new FakeAnalyzerReference("original", @"C:\Repro\Generator\obj\Debug\Generator.dll");
        var shadowReference = new FakeAnalyzerReference("shadow", @"C:\Temp\RoslynMcpServer.AnalyzerShadowCopy\Generator.dll");

        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
            .WithAnalyzerReferences(new AnalyzerReference[] { originalReference });
        var workspaceCurrentSolution = workspace.CurrentSolution.AddProject(projectInfo);
        var candidateSolution = workspaceCurrentSolution.WithProjectAnalyzerReferences(
            projectId,
            new AnalyzerReference[] { shadowReference });

        var manager = CreateManagerWithShadowCopyFlag(enabled: false);
        var result = InvokeRevertGuard(manager, candidateSolution, workspaceCurrentSolution);

        // Flag never enabled for this load: guard must not touch the candidate at all.
        Assert.Same(candidateSolution, result);
    }

    [Fact]
    public void RevertAnalyzerReferenceOverlayForApply_leaves_matching_references_untouched()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var sameReference = new FakeAnalyzerReference("same", @"C:\NuGet\SomeAnalyzers.dll");

        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
            .WithAnalyzerReferences(new AnalyzerReference[] { sameReference });
        var workspaceCurrentSolution = workspace.CurrentSolution.AddProject(projectInfo);

        // Candidate has no analyzer-reference diff from workspaceCurrentSolution (e.g. only a document edit).
        var candidateSolution = workspaceCurrentSolution;

        var manager = CreateManagerWithShadowCopyFlag(enabled: true);
        var result = InvokeRevertGuard(manager, candidateSolution, workspaceCurrentSolution);

        Assert.Same(candidateSolution, result);
    }

    private static SolutionManager CreateManagerWithShadowCopyFlag(bool enabled)
    {
        var manager = new SolutionManager(NullLogger<SolutionManager>.Instance);
        typeof(SolutionManager)
            .GetField("_shadowCopyAnalyzersEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, enabled);
        return manager;
    }

    private static Solution InvokeRevertGuard(SolutionManager manager, Solution candidate, Solution workspaceCurrentSolution)
    {
        var method = typeof(SolutionManager).GetMethod(
            "RevertAnalyzerReferenceOverlayForApply",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Solution)method.Invoke(manager, new object[] { candidate, workspaceCurrentSolution })!;
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
}
