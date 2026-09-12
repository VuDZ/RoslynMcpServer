using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class AnalyzerProvenanceCaptureGateTests
{
    [Fact]
    public void Null_snapshot_is_missing()
    {
        var session = Guid.NewGuid();
        Assert.False(AnalyzerProvenanceCaptureGate.IsSuitableForOptInOverlay(null, session));
        Assert.Equal(
            AnalyzerProvenanceCaptureGate.ReasonMissing,
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(null, session));
    }

    [Fact]
    public void Failed_and_incomplete_are_unsuitable()
    {
        var session = Guid.NewGuid();
        Assert.Equal(
            AnalyzerProvenanceCaptureGate.ReasonFailed,
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(Snapshot(session, AnalyzerProvenanceCaptureStatus.Failed), session));
        Assert.Equal(
            AnalyzerProvenanceCaptureGate.ReasonIncomplete,
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(Snapshot(session, AnalyzerProvenanceCaptureStatus.Incomplete), session));
    }

    [Fact]
    public void Session_mismatch_is_unsuitable_even_when_complete()
    {
        var session = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        Assert.Equal(
            AnalyzerProvenanceCaptureGate.ReasonSessionMismatch,
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
                Snapshot(foreign, AnalyzerProvenanceCaptureStatus.Complete),
                session));
        Assert.Equal(
            AnalyzerProvenanceCaptureGate.ReasonSessionMismatch,
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
                Snapshot(session, AnalyzerProvenanceCaptureStatus.Complete),
                Guid.Empty));
    }

    [Fact]
    public void Complete_same_session_is_suitable()
    {
        var session = Guid.NewGuid();
        Assert.True(
            AnalyzerProvenanceCaptureGate.IsSuitableForOptInOverlay(
                Snapshot(session, AnalyzerProvenanceCaptureStatus.Complete),
                session));
        Assert.Null(
            AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
                Snapshot(session, AnalyzerProvenanceCaptureStatus.Complete),
                session));
    }

    private static AnalyzerProvenanceSnapshot Snapshot(Guid sessionId, AnalyzerProvenanceCaptureStatus status)
    {
        return new AnalyzerProvenanceSnapshot(
            sessionId,
            @"C:\Repro\App.sln",
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            new AnalyzerProvenanceToolset(null, null, @"C:\msbuild.dll", null, null, null),
            status,
            System.Collections.Immutable.ImmutableArray<AnalyzerProvenanceProjectContext>.Empty,
            System.Collections.Immutable.ImmutableArray<CapturedAnalyzerProvenanceItem>.Empty,
            System.Collections.Immutable.ImmutableArray<AnalyzerProvenanceBinding>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            new AnalyzerProvenanceCaptureMetrics(0, 0, TimeSpan.Zero, 0, 0, 0));
    }
}
