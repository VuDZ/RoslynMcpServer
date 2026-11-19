using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

internal static class NarrowingAssert
{
    /// <summary>Stage N holds every class of stage N-1, with at least the reasons it had there.</summary>
    internal static void StagesAreNested(TestProjectNarrowing project)
    {
        if (project.Outcome == TestNarrowingOutcome.WholeProject)
        {
            Assert.NotNull(project.Whole);
            Assert.Empty(project.Stages);
            return;
        }

        Assert.Null(project.Whole);
        Assert.Equal(Enum.GetValues<TestClassStageLevel>(), project.Stages.Select(stage => stage.Level));
        for (var level = 1; level < project.Stages.Count; level++)
        {
            var wider = new Dictionary<VstestTestClass, TestClassSelection>(ReferenceEqualityComparer.Instance);
            foreach (var selection in project.Stages[level].Classes)
            {
                wider.Add(selection.Class, selection);
            }

            foreach (var selection in project.Stages[level - 1].Classes)
            {
                Assert.True(
                    wider.TryGetValue(selection.Class, out var widerSelection),
                    $"Stage {project.Stages[level].Level} lacks {selection.Class.RuntimeName} of stage {project.Stages[level - 1].Level}.");
                Assert.True(
                    widerSelection!.Reasons.HasFlag(selection.Reasons),
                    $"Stage {project.Stages[level].Level} lost a reason of {selection.Class.RuntimeName}.");
            }

            if (project.Stages[level - 1].Whole is not null)
            {
                Assert.NotNull(project.Stages[level].Whole);
            }
        }
    }

    /// <summary>Runtime names of the classes of a stage, ordered.</summary>
    internal static string[] Names(TestProjectNarrowing project, TestClassStageLevel level) =>
        [.. project.Stage(level).Classes.Select(selection => selection.Class.RuntimeName ?? "(none)").Order(StringComparer.Ordinal)];

    /// <summary>The classes of each stage, the narrowest first.</summary>
    internal static void Stages(TestProjectNarrowing project, string[] changed, string[] references, string[] discovery, string[] loads)
    {
        Assert.Equal(TestNarrowingOutcome.Narrowed, project.Outcome);
        Assert.Equal(changed.Order(StringComparer.Ordinal), Names(project, TestClassStageLevel.Changed));
        Assert.Equal(references.Order(StringComparer.Ordinal), Names(project, TestClassStageLevel.References));
        Assert.Equal(discovery.Order(StringComparer.Ordinal), Names(project, TestClassStageLevel.Discovery));
        Assert.Equal(loads.Order(StringComparer.Ordinal), Names(project, TestClassStageLevel.DiscoveryWithLoads));
    }

    internal static TestNarrowingWholeReason Whole(TestProjectNarrowing project, TestNarrowingWholeKind kind)
    {
        Assert.Equal(TestNarrowingOutcome.WholeProject, project.Outcome);
        var whole = Assert.IsType<TestNarrowingWholeReason>(project.Whole);
        Assert.Equal(kind, whole.Kind);
        Assert.False(string.IsNullOrWhiteSpace(whole.Message));
        return whole;
    }
}
