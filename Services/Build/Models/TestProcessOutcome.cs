namespace RoslynMcpServer.Services.Build;

internal enum TestProcessOutcome
{
    Passed,
    Failed,
    BuildFailed,
    TimedOut,
    Cancelled,
    NotRun
}
