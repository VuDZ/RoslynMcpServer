using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class TruncatedProcessLogTests
{
    [Fact]
    public void StripTrailingMsBuildOutcome_removes_vstest_test_failure_footer()
    {
        const string output = """
            Expected 1 to be 2
            Build FAILED.
                0 Warning(s)
                0 Error(s)

            Time Elapsed 00:00:12.34
            """;

        var stripped = TruncatedProcessLog.StripTrailingMsBuildOutcome(output);
        Assert.Contains("Expected 1 to be 2", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("Time Elapsed", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void StripTrailingMsBuildOutcome_keeps_payload_that_is_only_the_footer()
    {
        const string output = """
            Build FAILED.
                0 Warning(s)
                0 Error(s)
            """;

        Assert.Equal(output, TruncatedProcessLog.StripTrailingMsBuildOutcome(output));
    }

    [Fact]
    public void BuildTruncatedExcerpt_tail_is_assertion_not_zero_error_footer()
    {
        var body = "ASSERTION-HERE " + new string('q', 4000);
        var combined = body + """

            Build FAILED.
                0 Warning(s)
                0 Error(s)
            """;

        var excerpt = TruncatedProcessLog.BuildTruncatedExcerpt(combined);
        Assert.Contains("ASSERTION-HERE", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", excerpt, StringComparison.Ordinal);
    }
}
