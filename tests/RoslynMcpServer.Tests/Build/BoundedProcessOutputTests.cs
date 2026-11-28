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

        // Summary lines stay in the clipped capture: without a TRX report they are the only VSTest record.
        Assert.Contains("Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2", combined.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Failed!  - Failed: 1, Passed: 3, Skipped: 0, Total: 4", combined.CombinedOutput, StringComparison.Ordinal);

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
    public async Task Repeated_test_summaries_stay_within_the_capture_limit()
    {
        const string result = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n";
        using var reader = new StringReader(string.Concat(Enumerable.Repeat(result, 10_000)));
        var captured = await ReadOutputAsync(reader);

        Assert.True(captured.Truncated);
        Assert.Equal(result.Length * 10_000 - 1, captured.Length);
        Assert.InRange(captured.Text.Length, 1, BoundedProcessOutput.MaxStreamChars);
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

    [Theory]
    [InlineData("password=\n", "private-unquoted\n")]
    [InlineData("secret\n=\n", "private-split-assignment\n")]
    [InlineData("Bearer\n", "private-bearer\n")]
    [InlineData("Authorization:\n", "private-authorization\n")]
    public Task Cursor_never_contains_a_secret_whose_prefix_was_omitted(string prefix, string value) =>
        AssertCursorRedactsTruncatedSecretAsync(prefix, value);

    // VSTest prints theory arguments, so a compiler diagnostic in them creates a GitHub Actions error annotation.
    [Fact]
    public Task CursorRedactsTruncatedSecretContainingCompilerDiagnostic() =>
        AssertCursorRedactsTruncatedSecretAsync(
            "password='private-first\n",
            "fixture.cs(1,1): error CS9999: private-cross-line\nprivate-last'\n");

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

    private static async Task AssertCursorRedactsTruncatedSecretAsync(string prefix, string value)
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
