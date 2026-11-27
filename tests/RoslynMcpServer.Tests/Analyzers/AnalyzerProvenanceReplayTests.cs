using System.Collections;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Utilities;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Analyzers;
using Xunit;

namespace RoslynMcpServer.Tests.Analyzers;

public sealed class AnalyzerProvenanceReplayTests : IDisposable
{
    public AnalyzerProvenanceReplayTests()
    {
        MsBuildBootstrapper.Register();
        _directory = Path.Combine(Path.GetTempPath(), "ProvenanceReplay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Newer_format_preserves_contexts_and_analyzer_items()
    {
        var original = WriteLog("original.binlog", projectEvents: true);
        var newer = MakeNewer(original, "newer.binlog");
        var strict = new BinaryLogReplayEventSource();
        using var strictStream = File.OpenRead(newer);
        Assert.Throws<NotSupportedException>(() => strict.Replay(strictStream, CancellationToken.None));

        var baseline = AnalyzerProvenanceCaptureService.ReplayAll([original], CancellationToken.None);
        var compatible = AnalyzerProvenanceCaptureService.ReplayAll([newer], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Complete, compatible.Status);
        Assert.Empty(compatible.FailureCodes);
        Assert.Single(compatible.Contexts);
        Assert.Single(compatible.Items);
        Assert.Equal(JsonSerializer.Serialize(baseline.Contexts), JsonSerializer.Serialize(compatible.Contexts));
        Assert.Equal(JsonSerializer.Serialize(baseline.Items), JsonSerializer.Serialize(compatible.Items));
        Assert.Equal(baseline.Timings.MaxFormatVersion + 1, compatible.Timings.MaxFormatVersion);
    }

    [Fact]
    public void Newer_empty_buildhost_log_does_not_make_the_capture_incomplete()
    {
        var original = WriteLog("original.binlog", projectEvents: true);
        var empty = MakeNewer(WriteLog("empty.binlog", projectEvents: false), "empty-newer.binlog");
        var result = AnalyzerProvenanceCaptureService.ReplayAll([original, empty], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Complete, result.Status);
        Assert.Single(result.Contexts);
        Assert.Single(result.Items);
        using var json = JsonDocument.Parse(result.Timings.ToJson(100, result.FailureCodes));
        var files = json.RootElement.GetProperty("binlogs").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal(0, files[1].GetProperty("projectStarts").GetInt32());
        Assert.Equal(result.Timings.MaxFormatVersion, files[1].GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void Recoverable_errors_discard_file_evidence_and_count_failed_files_not_errors()
    {
        var original = WriteLog("original.binlog", projectEvents: true);
        var unknown = MakeNewer(original, "unknown.binlog", unknownRecords: 2);
        var failed = AnalyzerProvenanceCaptureService.ReplayAll([unknown], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Failed, failed.Status);
        Assert.Equal(2, failed.FailureCodes.Length);
        Assert.All(failed.FailureCodes, code => Assert.StartsWith("replay_recoverable:0:UnknownEventType:", code));
        Assert.Empty(failed.Contexts);
        Assert.Empty(failed.Items);

        var partial = AnalyzerProvenanceCaptureService.ReplayAll([original, unknown], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Incomplete, partial.Status);
        Assert.Single(partial.Contexts);
        Assert.Single(partial.Items);
        Assert.All(partial.FailureCodes, code => Assert.StartsWith("replay_recoverable:1:", code));
        using var json = JsonDocument.Parse(partial.Timings.ToJson(100, partial.FailureCodes));
        Assert.Equal("incomplete", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Missing_corrupt_and_cancelled_captures_keep_their_failure_behavior()
    {
        var missing = AnalyzerProvenanceCaptureService.ReplayAll([], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Failed, missing.Status);
        Assert.Equal("missing_binlog", Assert.Single(missing.FailureCodes));
        var corrupt = Path.Combine(_directory, "corrupt.binlog");
        File.WriteAllBytes(corrupt, [1, 2, 3, 4]);
        var failed = AnalyzerProvenanceCaptureService.ReplayAll([corrupt], CancellationToken.None);
        Assert.Equal(AnalyzerProvenanceCaptureStatus.Failed, failed.Status);
        Assert.StartsWith("replay_failed:0:", Assert.Single(failed.FailureCodes));
        Assert.Empty(failed.Contexts);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            AnalyzerProvenanceCaptureService.ReplayAll([corrupt], new CancellationToken(true)));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteLog(string name, bool projectEvents)
    {
        var path = Path.Combine(_directory, name);
        var source = new SyntheticEventSource();
        var logger = new BinaryLogger { Parameters = path + ";ProjectImports=None", Verbosity = LoggerVerbosity.Diagnostic };
        logger.Initialize(source);
        var start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        source.Raise(new BuildStartedEventArgs("Build started", "", start));
        if (projectEvents)
        {
            var project = Path.Combine(_directory, "Consumer.csproj");
            var context = new BuildEventContext(1, 1, 1, 1);
            source.Raise(new ProjectStartedEventArgs("Project started", "", project, "Compile",
                new[] { new DictionaryEntry("TargetFramework", "net10.0") }, null!, start.AddMilliseconds(10))
                { BuildEventContext = context });
            var analyzer = new TaskItem(Path.Combine(_directory, "Generator.dll"));
            analyzer.SetMetadata("MSBuildSourceProjectFile", Path.Combine(_directory, "Generator.csproj"));
            analyzer.SetMetadata("NearestTargetFramework", "net10.0");
            source.Raise(new TaskParameterEventArgs(TaskParameterMessageKind.TaskOutput, "Analyzer",
                new ArrayList { analyzer }, true, start.AddMilliseconds(20)) { BuildEventContext = context });
            source.Raise(new ProjectFinishedEventArgs("Project finished", "", project, true, start.AddMilliseconds(30))
                { BuildEventContext = context });
        }
        source.Raise(new BuildFinishedEventArgs("Build finished", "", true, start.AddMilliseconds(40)));
        logger.Shutdown();
        return path;
    }

    private string MakeNewer(string source, string name, int unknownRecords = 0)
    {
        byte[] bytes;
        using (var input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        BitConverter.GetBytes(BitConverter.ToInt32(bytes) + 1).CopyTo(bytes, 0);
        Assert.Equal(0, bytes[^1]);
        var target = Path.Combine(_directory, name);
        using var output = new GZipStream(File.Create(target), CompressionLevel.Fastest);
        output.Write(bytes.AsSpan(0, bytes.Length - 1));
        for (var index = 0; index < unknownRecords; index++)
        {
            output.Write([0xfe, 0x01, 0x00]); // Length-framed unknown record 254, with an empty payload.
        }
        output.WriteByte(0);
        return target;
    }

    private sealed class SyntheticEventSource : IEventSource
    {
        public event AnyEventHandler? AnyEventRaised;
#pragma warning disable CS0067 // BinaryLogger subscribes only to AnyEventRaised in this fixture.
        public event BuildStartedEventHandler? BuildStarted;
        public event BuildFinishedEventHandler? BuildFinished;
        public event ProjectStartedEventHandler? ProjectStarted;
        public event ProjectFinishedEventHandler? ProjectFinished;
        public event TargetStartedEventHandler? TargetStarted;
        public event TargetFinishedEventHandler? TargetFinished;
        public event TaskStartedEventHandler? TaskStarted;
        public event TaskFinishedEventHandler? TaskFinished;
        public event BuildMessageEventHandler? MessageRaised;
        public event BuildWarningEventHandler? WarningRaised;
        public event BuildErrorEventHandler? ErrorRaised;
        public event CustomBuildEventHandler? CustomEventRaised;
        public event BuildStatusEventHandler? StatusEventRaised;
#pragma warning restore CS0067

        public void Raise(BuildEventArgs args) => AnyEventRaised?.Invoke(this, args);
    }

    private readonly string _directory;
}
