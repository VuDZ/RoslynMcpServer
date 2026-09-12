using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class AnalyzerShadowPublicationPlannerTests
{
    [Fact]
    public void Confirmed_overlay_excludes_foreign_and_unconfirmed()
    {
        var projectId = ProjectId.CreateNewId();
        Assert.False(AnalyzerShadowPublicationPlanner.IsConfirmedOverlay(Entry(
            projectId,
            applied: false,
            reason: AnalyzerReferenceReasonCodes.ProvenForeignPath)));
        Assert.False(AnalyzerShadowPublicationPlanner.IsConfirmedOverlay(Entry(
            projectId,
            applied: false,
            reason: AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed)));
        Assert.True(AnalyzerShadowPublicationPlanner.IsConfirmedOverlay(Entry(
            projectId,
            applied: false,
            reason: AnalyzerReferenceReasonCodes.PreparationFailure,
            basis: AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput)));
    }

    [Fact]
    public void Restart_latch_reports_prepared_files_as_not_applied()
    {
        var projectId = ProjectId.CreateNewId();
        var mapping = new AnalyzerShadowMapping(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            [
                Entry(
                    projectId,
                    applied: true,
                    reason: AnalyzerReferenceReasonCodes.ReferenceRewritten,
                    shadow: @"C:\Shadow\Generator.dll"),
            ]);
        var prepared = new AnalyzerShadowPrepareOutcome(
            mapping,
            AnalyzerReferenceShadowCopier.ToRewriteResults(mapping),
            RefreshSucceeded: true,
            UsedPreviousMappingAsStale: false,
            FailureSummary: null);

        var plan = AnalyzerShadowPublicationPlanner.Evaluate(
            prepared,
            new InProcessAnalyzerAssemblyLoader(),
            restartBanLatched: true);
        Assert.Equal(AnalyzerShadowPublicationKind.Banned, plan.Kind);
        Assert.Equal(0, plan.AppliedCount);
        Assert.True(plan.PreparedCount >= 1);
        Assert.True(plan.BlockedCount >= 1);

        var results = AnalyzerShadowPublicationPlanner.ToRewriteResults(prepared.Results, plan);
        Assert.All(results, result => Assert.False(result.Applied));
        var summary = AnalyzerShadowPublicationPlanner.FormatLoadSummary(plan, plan.Gate);
        Assert.Contains("prepared files were not applied", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applied=0", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void All_failed_confirmed_entries_ban_without_claiming_full_refresh()
    {
        var projectId = ProjectId.CreateNewId();
        var mapping = new AnalyzerShadowMapping(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            [
                Entry(
                    projectId,
                    applied: false,
                    reason: AnalyzerReferenceReasonCodes.PreparationFailure,
                    basis: AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput,
                    original: @"C:\Repro\Generator.dll"),
                Entry(
                    projectId,
                    applied: false,
                    reason: AnalyzerReferenceReasonCodes.PreparationFailure,
                    basis: AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput,
                    original: @"C:\Repro\GeneratorB.dll"),
            ]);
        var prepared = new AnalyzerShadowPrepareOutcome(
            mapping,
            AnalyzerReferenceShadowCopier.ToRewriteResults(mapping),
            RefreshSucceeded: false,
            UsedPreviousMappingAsStale: false,
            FailureSummary: "publish-failed");

        var plan = AnalyzerShadowPublicationPlanner.Evaluate(
            prepared,
            new InProcessAnalyzerAssemblyLoader(),
            restartBanLatched: false);
        Assert.Equal(AnalyzerShadowPublicationKind.Banned, plan.Kind);
        Assert.Equal(0, plan.AppliedCount);
        Assert.Equal(2, plan.BlockedCount);
        Assert.False(plan.RefreshComplete);
        Assert.Equal(2, plan.Exclusions.Count);
        var summary = AnalyzerShadowPublicationPlanner.FormatLoadSummary(plan, plan.Gate);
        Assert.DoesNotContain("2 rewritten", summary, StringComparison.Ordinal);
        Assert.Contains("blocked=2", summary, StringComparison.Ordinal);
    }

    private static AnalyzerShadowReferenceEntry Entry(
        ProjectId projectId,
        bool applied,
        string reason,
        AnalyzerReferenceSelectionBasis basis = AnalyzerReferenceSelectionBasis.None,
        string? shadow = null,
        string? original = @"C:\Repro\Generator.dll")
    {
        return new AnalyzerShadowReferenceEntry(
            projectId,
            "Consumer",
            "analyzer",
            original,
            "Generator",
            shadow,
            applied ? "gen-1" : null,
            applied,
            applied ? null : "prepare-failed",
            StaleGeneration: false,
            reason,
            SelectionBasis: basis);
    }
}
