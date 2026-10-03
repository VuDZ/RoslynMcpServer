namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record DependencyContextObservation(string Project, int InstanceId, Dictionary<string, string> Properties);
