namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record TestImpactEstimate(
    IReadOnlyList<DocumentImpact> Documents,
    IReadOnlyList<string> ChangedPaths,
    TestImpactResult Impact,
    IReadOnlyList<VstestProjectRunPlan> Plans,
    IReadOnlyList<VstestTestClass> Classes,
    IReadOnlyList<string> NotCovered,
    IReadOnlyList<string> CompilationErrors);
