namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record TestImpactEstimate(
    IReadOnlyList<DocumentImpact> Documents,
    IReadOnlyList<string> ChangedPaths,
    TestImpactResult Impact,
    IReadOnlyList<VstestProjectRunPlan> Plans,
    IReadOnlyList<VstestTestClass> Classes,
    IReadOnlyList<string> NotCovered,
    IReadOnlyList<string> CompilationErrors)
{
    /// <summary>
    /// The solution path of the baseline the estimate was computed against; null when no baseline was
    /// published. With <see cref="BaselinePublishedAtUtc"/> it names that baseline: a reload of the same
    /// solution keeps both, a new baseline changes them.
    /// </summary>
    internal string? BaselineWorkspacePath { get; init; }

    internal DateTime BaselinePublishedAtUtc { get; init; }

    internal IReadOnlyCollection<string> UnknownBaselinePaths { get; init; } = [];
}
