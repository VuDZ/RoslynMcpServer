namespace RoslynMcpServer.Tests.Support;

internal static class TestEnvironmentLocks
{
    internal static readonly object DotNetRoot = new();
}
