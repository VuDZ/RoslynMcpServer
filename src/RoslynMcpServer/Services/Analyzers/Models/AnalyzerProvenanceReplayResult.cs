using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Analyzers;

internal sealed record AnalyzerProvenanceReplayResult(
    AnalyzerProvenanceCaptureStatus Status,
    ImmutableArray<AnalyzerProvenanceProjectContext> Contexts,
    ImmutableArray<CapturedAnalyzerProvenanceItem> Items,
    ImmutableArray<string> FailureCodes,
    DesignTimeBuildTimings Timings);
