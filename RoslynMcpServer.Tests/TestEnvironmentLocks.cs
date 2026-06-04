namespace RoslynMcpServer.Tests;

internal static class TestEnvironmentLocks
{
    internal static readonly object DotNetRoot = new();
}
