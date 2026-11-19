using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Build;

internal sealed record DotNetTestResult(
    TestProcessOutcome Outcome,
    DotNetCliRunner.RunResult? BuildRun = null,
    DotNetCliRunner.RunResult? TestRun = null,
    VstestOutputParser.ParseResult? Parsed = null,
    string? Error = null)
{
    internal string OutcomeText => Outcome switch
    {
        TestProcessOutcome.Passed => "passed",
        TestProcessOutcome.Failed => "failed",
        TestProcessOutcome.BuildFailed => "build failed",
        TestProcessOutcome.TimedOut => "timed out",
        TestProcessOutcome.Cancelled => "cancelled",
        _ => "not run"
    };
}
