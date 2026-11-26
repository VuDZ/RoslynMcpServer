using RoslynMcpServer.Services.Build;

namespace RoslynMcpServer.Diagnostics;

internal sealed record VstestTrxResult(
    TestProcessOutcome Outcome,
    VstestOutputParser.TestSummary? Summary,
    IReadOnlyList<VstestOutputParser.FailedTestDetail> Failures,
    IReadOnlyList<string> PassedTestNames,
    bool FailureDetailsTruncated,
    bool NoFilterMatches,
    string? Issue,
    string? BuildError);
