namespace RoslynMcpServer.Services.Testing.Baseline.Models;

/// <summary>Reload-stable membership using loaded project names/occurrences and logical paths, never Roslyn IDs.</summary>
internal sealed record BaselineDocumentKey(
    string ProjectPath,
    string ProjectContext,
    string DocumentPath,
    string LogicalPath,
    int Occurrence);
