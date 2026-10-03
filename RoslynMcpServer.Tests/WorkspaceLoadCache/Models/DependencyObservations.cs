namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed class DependencyObservations
{
    public List<DependencyImportObservation> Imports { get; } = [];
    public List<string> TaskOutputAnalyzers { get; } = [];
    public List<string> TaskOutputOwners { get; } = [];
    public List<string> CompilerCommands { get; } = [];
    public List<string> CompilerOwners { get; } = [];
    public List<DependencyContextObservation> ProjectContexts { get; } = [];
    public int CoreCompileCount { get; set; }
}
