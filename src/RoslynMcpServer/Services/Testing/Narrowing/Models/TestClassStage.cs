namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>The test classes of one stage, including those of the previous stages, in inventory order.</summary>
/// <param name="Filter">The VSTest filter of the classes; null when the stage is empty or runs whole.</param>
/// <param name="Whole">
/// Set when the stage cannot be run as a class filter: the project runs whole for this stage and the wider ones.
/// </param>
internal sealed record TestClassStage(
    TestClassStageLevel Level,
    IReadOnlyList<TestClassSelection> Classes,
    string? Filter,
    TestNarrowingWholeReason? Whole)
{
    internal bool SelectsTests => Whole is not null || Classes.Count > 0;
}
