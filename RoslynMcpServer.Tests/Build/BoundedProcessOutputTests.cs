using System.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

[Collection("DiagnosticReportStore")]
public sealed class BoundedProcessOutputTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    public async Task Both_runner_modes_drain_noisy_pipes_and_keep_diagnostics_under_the_same_limit(int repetitions)
    {
        await using var fixture = new DotNetPipeFixture();
        var command = fixture.NoisyCommand(repetitions);
        var timeout = TimeSpan.FromSeconds(30);
        var combined = await DotNetCliRunner.RunWithMetadataAsync(
            command, fixture.WorkingDirectory, CancellationToken.None, timeout);
        var separated = await DotNetCliRunner.RunSeparatedAsync(command, fixture.WorkingDirectory, timeout);

        Assert.Equal(17, combined.ExitCode);
        Assert.Equal(17, separated.ExitCode);
        Assert.False(combined.TimedOut);
        Assert.False(separated.TimedOut);
        Assert.Null(separated.ExceptionType);
        Assert.True(combined.OutputTruncated);
        Assert.True(separated.OutputTruncated);
        Assert.True(combined.StdOutLength > repetitions * 2048);
        Assert.True(combined.StdErrLength > repetitions * 2048);
        Assert.InRange(separated.StdOut.Length, 1, BoundedProcessOutput.MaxStreamChars);
        Assert.InRange(separated.StdErr.Length, 1, BoundedProcessOutput.MaxStreamChars);
        Assert.Equal(Combine(separated), combined.CombinedOutput);
        Assert.StartsWith("stdout-head", separated.StdOut, StringComparison.Ordinal);
        Assert.StartsWith("stderr-head", separated.StdErr, StringComparison.Ordinal);
        Assert.EndsWith("stdout-tail", separated.StdOut, StringComparison.Ordinal);
        Assert.EndsWith("stderr-tail Bearer private-tail", separated.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("private-oversized-secret", separated.StdErr, StringComparison.Ordinal);

        var parsed = VstestOutputParser.Parse(
            combined.CombinedOutput, combined.ExitCode, combined.TestSummaryTruncated, combined.OutputTruncated);
        Assert.Equal(new VstestOutputParser.TestSummary(6, 5, 1, 0), parsed.Summary);

        var report = BuildOutputReport.Format(
            combined.CombinedOutput, combined.ExitCode, "fixture", includeBuildWarnings: true);
        Assert.Contains("**warning** `CS0168`", report, StringComparison.Ordinal);
        Assert.Contains("**error** `CS1002`", report, StringComparison.Ordinal);
        Assert.Contains("Exit code: `17`.", report, StringComparison.Ordinal);
        var cursor = report.Split("reportCursor=`", StringSplitOptions.None)[1].Split('`')[0];
        var body = ReadCursor(cursor);
        Assert.Contains("stdout-head", body, StringComparison.Ordinal);
        Assert.Contains("stderr-head", body, StringComparison.Ordinal);
        Assert.Contains(TruncatedProcessLog.CaptureMarker, body, StringComparison.Ordinal);
        Assert.Contains("stdout-tail", body, StringComparison.Ordinal);
        Assert.EndsWith("stderr-tail Bearer [redacted]", body, StringComparison.Ordinal);
        Assert.Contains("password=[redacted]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", body, StringComparison.Ordinal);
        Assert.False(DiagnosticReportStore.TryTakeChunk(cursor).Ok);
    }

    [Fact]
    public async Task Exhausted_test_summary_budget_produces_a_partial_result()
    {
        const string result = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n";
        using var reader = new StringReader(string.Concat(Enumerable.Repeat(result, 10_000)));
        var captured = await ReadOutputAsync(reader);

        Assert.True(captured.SummaryTruncated);
        Assert.Equal(result.Length * 10_000 - 1, captured.Length);
        Assert.InRange(captured.Text.Length, 1, BoundedProcessOutput.MaxStreamChars);
        var parsed = VstestOutputParser.Parse(captured.Text, exitCode: 0, captured.SummaryTruncated, captured.Truncated);
        var report = VstestOutputParser.BuildMarkdownReport(parsed, 0, captured.Text, "Some", null, true);
        Assert.Null(parsed.Summary);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("test summary exceeded the capture limit", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_build_with_truncated_capture_offers_a_cursor()
    {
        var processLog = $"head\n{TruncatedProcessLog.CaptureMarker}\ntail";
        var report = BuildOutputReport.Format(processLog, 0, "fixture", includeBuildWarnings: false);

        Assert.StartsWith("## Build succeeded", report, StringComparison.Ordinal);
        Assert.Contains("Process output was truncated during capture.", report, StringComparison.Ordinal);
        var cursor = report.Split("reportCursor=`", StringSplitOptions.None)[1].Split('`')[0];
        Assert.Equal(processLog, ReadCursor(cursor));
    }

    [Fact]
    public async Task Filtered_report_is_partial_when_capture_drops_the_matching_test_line()
    {
        var noise = string.Concat(Enumerable.Repeat(new string('x', 1023) + "\n", 150));
        const string test = "  Passed Fixture.Tests.Match [1 ms]\n";
        const string summary = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n";
        using var reader = new StringReader(noise + test + noise + summary);
        var captured = await ReadOutputAsync(reader);
        var parsed = VstestOutputParser.Parse(captured.Text, 0, captured.SummaryTruncated, captured.Truncated);

        Assert.True(captured.Truncated);
        Assert.Equal(1, parsed.Summary?.Total);
        Assert.DoesNotContain("Fixture.Tests.Match", captured.Text, StringComparison.Ordinal);
        var report = VstestOutputParser.BuildMarkdownReport(
            parsed, 0, captured.Text, "FullyQualifiedName~Fixture.Tests.Match", null, requireFilterMatch: true);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("Retained test names do not verify the filter match", report, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Filtered_report_preserves_a_confirmed_zero_match_after_capture()
    {
        var log = TruncatedProcessLog.CaptureMarker + "\n"
            + "No test matches the given testcase filter\n"
            + "Passed! - Failed: 0, Passed: 0, Skipped: 0, Total: 0";
        var parsed = VstestOutputParser.Parse(log, 0, summaryTruncated: false, outputTruncated: true);

        var report = VstestOutputParser.BuildMarkdownReport(
            parsed, 0, log, "FullyQualifiedName~Fixture.Tests.Missing", null, requireFilterMatch: true);

        Assert.Contains("no matching tests", report, StringComparison.Ordinal);
        Assert.DoesNotContain("**Status:** partial", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password=\n", "private-unquoted\n")]
    [InlineData("secret\n=\n", "private-split-assignment\n")]
    [InlineData("password='private-first\n", "fixture.cs(1,1): error CS9999: private-cross-line\nprivate-last'\n")]
    [InlineData("Bearer\n", "private-bearer\n")]
    [InlineData("Authorization:\n", "private-authorization\n")]
    public async Task Cursor_never_contains_a_secret_whose_prefix_was_omitted(string prefix, string value)
    {
        const string tail = "unrelated-tail\n";
        var noise = string.Concat(Enumerable.Repeat(new string('x', 1023) + "\n", 300));
        var tailPadding = new string('t', 48_000 - value.Length - tail.Length - 1) + "\n";
        using var reader = new StringReader("head\n" + noise + prefix + value + tailPadding + tail);
        var captured = await ReadOutputAsync(reader);

        Assert.True(captured.Truncated);
        var stored = DiagnosticReportStore.TryStore(captured.Text);
        var body = ReadCursor(stored.CursorId!);
        Assert.Contains("head", body, StringComparison.Ordinal);
        Assert.Contains(TruncatedProcessLog.CaptureMarker, body, StringComparison.Ordinal);
        Assert.EndsWith("unrelated-tail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public async Task Combined_and_split_cursors_redact_secrets_crossing_the_pipes(
        bool capStdout,
        bool capStderr,
        bool quoted)
    {
        await using var fixture = new DotNetPipeFixture();
        var command = fixture.CrossPipeCommand(capStdout, capStderr, quoted);
        var timeout = TimeSpan.FromSeconds(30);
        var combined = await DotNetCliRunner.RunWithMetadataAsync(
            command, fixture.WorkingDirectory, CancellationToken.None, timeout);
        var separated = await DotNetCliRunner.RunSeparatedAsync(command, fixture.WorkingDirectory, timeout);

        Assert.Equal(17, combined.ExitCode);
        Assert.Equal(17, separated.ExitCode);
        Assert.True(combined.OutputTruncated);
        Assert.True(separated.OutputTruncated);
        Assert.Equal(combined.CombinedOutput, Combine(separated));
        foreach (var text in new[] { combined.CombinedOutput, Combine(separated) })
        {
            var report = BuildOutputReport.Format(text, 17, "fixture", includeBuildWarnings: true);
            var cursor = report.Split("reportCursor=`", StringSplitOptions.None)[1].Split('`')[0];
            var body = ReadCursor(cursor);
            Assert.Contains("stdout-anchor", body, StringComparison.Ordinal);
            Assert.Contains("stderr-anchor", body, StringComparison.Ordinal);
            Assert.Contains("unrelated-diagnostic", body, StringComparison.Ordinal);
            Assert.DoesNotContain("private-", body, StringComparison.Ordinal);
        }
    }

    private static async Task<BoundedProcessOutput.Capture> ReadOutputAsync(TextReader reader)
    {
        var output = new BoundedProcessOutput(reader);
        await output.Completion;
        return output.Finish();
    }

    private static string Combine(DotNetCliRunner.SeparatedRunResult run) =>
        run.StdOut + Environment.NewLine + run.StdErr;

    private static string ReadCursor(string cursor)
    {
        var body = new StringBuilder();
        DiagnosticReportStore.TakeOutcome chunk;
        do
        {
            chunk = DiagnosticReportStore.TryTakeChunk(cursor);
            Assert.True(chunk.Ok, chunk.Error);
            body.Append(chunk.Chunk);
        }
        while (chunk.HasMore);

        return body.ToString();
    }
}
