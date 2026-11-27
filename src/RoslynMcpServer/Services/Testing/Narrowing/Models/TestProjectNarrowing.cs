using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>The answer for one test project that epoch 1 selected whole.</summary>
/// <param name="Whole">Set exactly when <paramref name="Outcome"/> is <see cref="TestNarrowingOutcome.WholeProject"/>.</param>
/// <param name="Stages">Four stages in the order of <see cref="TestClassStageLevel"/>; empty when the project stays whole.</param>
/// <param name="ChangedTypes">
/// Changed, added and removed top-level types of the assemblies on the path to the project, every type of an
/// assembly whose own fingerprint changed included. Empty when the project stays whole.
/// </param>
/// <param name="ChangedAssemblies">Assemblies on the path whose own fingerprint (resources, attributes) changed.</param>
internal sealed record TestProjectNarrowing(
    ProjectId ProjectId,
    TestNarrowingOutcome Outcome,
    TestNarrowingWholeReason? Whole,
    IReadOnlyList<TestClassStage> Stages,
    IReadOnlyList<IlTypeName> ChangedTypes,
    IReadOnlyList<string> ChangedAssemblies)
{
    internal TestClassStage Stage(TestClassStageLevel level) => Stages[(int)level];

    internal TestClassStage? SelectedStage(TestClassStageLevel level) => Whole is null ? Stage(level) : null;
}
