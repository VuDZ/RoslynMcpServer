using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class TestImpactFormatterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_wider_stage_blocker_keeps_counts_visible_and_only_affects_its_stage_and_wider(bool wide)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Tests", LanguageNames.CSharp);
        const string Filter = "FullyQualifiedName~Cases.Direct.";
        var direct = new VstestTestClass(project.Id, [], "Cases.Direct", true, null, false);
        var fixture = new VstestTestClass(project.Id, [], "Cases.Fixture", true, null, true);
        TestClassSelection[] directClasses = [new(direct, TestClassSelectionReasons.ReachesChangedType)];
        TestClassSelection[] discoveryClasses = [.. directClasses, new(fixture, TestClassSelectionReasons.ReachesChangedType)];
        var whole = new TestNarrowingWholeReason(TestNarrowingWholeKind.FixtureArguments, "Cases.Fixture",
            "Cases.Fixture has fixture arguments.");
        TestClassStage[] stages =
        [
            new(TestClassStageLevel.Changed, [], null, null),
            new(TestClassStageLevel.References, directClasses, Filter, null),
            new(TestClassStageLevel.Discovery, discoveryClasses, null, whole),
            new(TestClassStageLevel.DiscoveryWithLoads, discoveryClasses, null, whole),
        ];
        var narrowing = new TestNarrowingResult([new TestProjectNarrowing(project.Id, TestNarrowingOutcome.Narrowed,
            null, stages, [new("Lib", "Lib.A")], [])]);
        var impact = new TestImpactResult([], new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>>(), [],
            RequiresReload: false, ProjectGraphStale: false);
        var estimate = new TestImpactEstimate([], [], impact, [new(project.Id, null, ["Project selected."])],
            [direct, fixture], [], []);
        var depth = wide ? TestClassStageLevel.Discovery : TestClassStageLevel.References;

        var response = TestImpactFormatter.Format(project.Solution, estimate, narrowing, depth,
            includeDetails: true, runs: null, state: TestImpactDepth.State(depth));

        Assert.Contains("Stages (classes): changed=0, direct=1, di=2 (whole project), all=2 (whole project).",
            response, StringComparison.Ordinal);
        Assert.Contains("di (2, whole project): Cases.Direct, Cases.Fixture", response, StringComparison.Ordinal);
        if (wide)
        {
            Assert.Contains("whole project", response, StringComparison.Ordinal);
            Assert.Contains(whole.Message, response, StringComparison.Ordinal);
            Assert.DoesNotContain(Filter, response, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(Filter, response, StringComparison.Ordinal);
            Assert.DoesNotContain(whole.Message, response, StringComparison.Ordinal);
        }
    }
}
