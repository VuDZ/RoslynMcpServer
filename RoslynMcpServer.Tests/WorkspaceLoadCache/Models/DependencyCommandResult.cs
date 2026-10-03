namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record DependencyCommandResult(int ExitCode, string StandardOutput, string StandardError);
