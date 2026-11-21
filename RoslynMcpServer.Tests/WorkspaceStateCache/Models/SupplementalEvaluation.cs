namespace RoslynMcpServer.Tests.WorkspaceStateCache.Models;

/// <summary>A separate evaluation identity; it does not attest a Roslyn BuildHost consumer.</summary>
internal sealed record SupplementalEvaluation(
    string ProjectPath,
    int EvaluationId,
    IReadOnlyDictionary<string, string> Globals,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> Imports,
    IReadOnlyList<string> Compile,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> ReferencePaths,
    IReadOnlyList<string> RestoredConfigPaths);
