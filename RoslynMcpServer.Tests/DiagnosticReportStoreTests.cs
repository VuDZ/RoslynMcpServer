using System.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DiagnosticReportStoreTests : IDisposable
{
    public DiagnosticReportStoreTests()
    {
        DiagnosticReportStore.ResetForTests();
    }

    public void Dispose()
    {
        DiagnosticReportStore.ResetForTests();
    }

    [Theory]
    [InlineData("password=hunter2", "password=[redacted]")]
    [InlineData("PWD: secretvalue", "PWD: [redacted]")]
    [InlineData("token = abc.def", "token = [redacted]")]
    [InlineData("secret:\"quoted\"", "secret:[redacted]")]
    [InlineData("apiKey=xyz", "apiKey=[redacted]")]
    [InlineData("api_key: 123", "api_key: [redacted]")]
    [InlineData("Authorization: Bearer deadbeef", "Authorization: [redacted]")]
    [InlineData("Authorization=Basic abc", "Authorization=[redacted]")]
    public void Redact_assignment_secrets(string input, string expected)
    {
        Assert.Equal(expected, ProcessOutputRedactor.Redact(input));
    }

    [Fact]
    public void Redact_standalone_bearer_token()
    {
        var redacted = ProcessOutputRedactor.Redact("header Bearer eyJhbGciOiJIUzI1NiJ9.payload");
        Assert.Contains("Bearer [redacted]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_nuget_userinfo_url_password()
    {
        var redacted = ProcessOutputRedactor.Redact(
            "GET https://user:SuperSecret@nuget.example.com/v3/index.json");
        Assert.Contains("https://user:[redacted]@", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void TryStore_then_TryTakeChunk_returns_chunks_then_exhaustion_errors()
    {
        var payload = new string('a', DiagnosticReportStore.ChunkChars + 40);
        var stored = DiagnosticReportStore.TryStore(payload);
        Assert.Equal(DiagnosticReportStore.StoreStatus.Stored, stored.Status);
        Assert.False(string.IsNullOrWhiteSpace(stored.CursorId));

        var first = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
        Assert.True(first.Ok);
        Assert.True(first.HasMore);
        Assert.Equal(DiagnosticReportStore.ChunkChars, first.Chunk!.Length);

        var second = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
        Assert.True(second.Ok);
        Assert.False(second.HasMore);
        Assert.Equal(new string('a', 40), second.Chunk);

        var third = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
        Assert.False(third.Ok);
        Assert.Contains("unknown or expired", third.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_cursor_returns_human_error()
    {
        var take = DiagnosticReportStore.TryTakeChunk("deadbeefcafebabe");
        Assert.False(take.Ok);
        Assert.StartsWith("Error:", take.Error);
        Assert.Contains("reportCursor", take.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Over_cap_report_is_prefix_plus_explicit_marker()
    {
        var huge = new string('x', DiagnosticReportStore.MaxReportChars + 50_000);
        var stored = DiagnosticReportStore.TryStore(huge);
        Assert.Equal(DiagnosticReportStore.StoreStatus.Stored, stored.Status);
        Assert.True(stored.StoredChars <= DiagnosticReportStore.MaxReportChars);

        var take = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
        Assert.True(take.Ok);
        var rebuilt = new StringBuilder(take.Chunk);
        while (take.HasMore)
        {
            take = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
            Assert.True(take.Ok);
            rebuilt.Append(take.Chunk);
        }

        var body = rebuilt.ToString();
        Assert.True(body.Length <= DiagnosticReportStore.MaxReportChars);
        Assert.Contains(DiagnosticReportStore.CapMarker.Trim(), body, StringComparison.Ordinal);
        Assert.StartsWith(new string('x', 100), body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', DiagnosticReportStore.MaxReportChars + 1), body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryStore_redacts_before_storing()
    {
        var stored = DiagnosticReportStore.TryStore("password=hunter2 and Bearer tok123");
        var take = DiagnosticReportStore.TryTakeChunk(stored.CursorId);
        Assert.True(take.Ok);
        Assert.Contains("password=[redacted]", take.Chunk, StringComparison.Ordinal);
        Assert.Contains("Bearer [redacted]", take.Chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", take.Chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("tok123", take.Chunk, StringComparison.Ordinal);
    }

    [Fact]
    public void Evicts_oldest_when_over_entry_count()
    {
        var ids = new List<string>();
        for (var i = 0; i < DiagnosticReportStore.MaxEntries + 1; i++)
        {
            var stored = DiagnosticReportStore.TryStore($"report-{i}");
            Assert.Equal(DiagnosticReportStore.StoreStatus.Stored, stored.Status);
            ids.Add(stored.CursorId!);
        }

        var oldest = DiagnosticReportStore.TryTakeChunk(ids[0]);
        Assert.False(oldest.Ok);

        var newest = DiagnosticReportStore.TryTakeChunk(ids[^1]);
        Assert.True(newest.Ok);
        Assert.Equal("report-" + DiagnosticReportStore.MaxEntries, newest.Chunk);
    }
}

public sealed class DiagnosticReportAttachmentTests : IDisposable
{
    public DiagnosticReportAttachmentTests()
    {
        DiagnosticReportStore.ResetForTests();
    }

    public void Dispose()
    {
        DiagnosticReportStore.ResetForTests();
    }

    [Fact]
    public void Truncated_response_attaches_cursor()
    {
        var combined = new string('z', TruncatedProcessLog.DefaultMaxCombinedCharacters + 500);
        var response = "excerpt" + TruncatedProcessLog.MiddleMarker + "tail";
        var attached = DiagnosticReportAttachment.AttachToResponse(
            response,
            combined,
            DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(response));

        Assert.Contains("reportCursor=`", attached, StringComparison.Ordinal);
        Assert.Contains("Full report:", attached, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_status_attaches_cursor()
    {
        const string response = "**Status:** partial\n\nTests completed (exit 0); no VSTest/xUnit summary line was detected.";
        var attached = DiagnosticReportAttachment.AttachToResponse(
            response,
            "raw-combined-output",
            DiagnosticReportAttachment.IsPartialOrUnparsedTestStatus(response));

        Assert.Contains("reportCursor=`", attached, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_all_pass_report_does_not_attach_cursor()
    {
        const string response = "## All tests passed successfully!\n\nTotal: **1** · Passed: **1** · Failed: **0**";
        var shouldStore = DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(response)
                          || DiagnosticReportAttachment.IsPartialOrUnparsedTestStatus(response);
        Assert.False(shouldStore);

        var attached = DiagnosticReportAttachment.AttachToResponse(response, "short log", shouldStore);
        Assert.Equal(response, attached);
        Assert.DoesNotContain("reportCursor", attached, StringComparison.Ordinal);
    }
}
