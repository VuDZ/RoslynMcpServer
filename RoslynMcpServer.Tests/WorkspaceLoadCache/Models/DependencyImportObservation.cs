namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record DependencyImportObservation(string Project, string Imported, string Unexpanded, bool Ignored, string Message);
