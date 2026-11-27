using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class VstestOutputParserTests
{
    [Fact]
    public void TestOutputReportOptions_resolves_default_full_and_explicit_caps()
    {
        Assert.Equal(2500, TestOutputReportOptions.Default.StdOutBudget);
        Assert.Equal(1000, TestOutputReportOptions.Default.StdErrBudget);

        var full = new TestOutputReportOptions(true, 0);
        Assert.Equal(100_000, full.StdOutBudget);
        Assert.Equal(100_000, full.StdErrBudget);

        var capped = new TestOutputReportOptions(true, 8_000);
        Assert.Equal(8_000, capped.StdOutBudget);
        Assert.Equal(3_200, capped.StdErrBudget);
    }

    [Fact]
    public void IsSilentUnparsedFailure_distinguishes_restore_failures_from_test_execution()
    {
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(string.Empty));
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure("Restore target(s) failed.\nBuild FAILED.\n    0 Error(s)"));
        Assert.False(VstestOutputParser.IsSilentUnparsedFailure("""
            Test Run Failed.
            Total tests: 1
                 Failed: 1
            """));
    }

    [Fact]
    public void Trx_failure_report_keeps_a_multiline_assertion_and_standard_output()
    {
        var error = "Expected notification to be equivalent to\n{\n    MessageType = OrderPaymentRefused,\n    "
            + new string('x', 700) + "\n}\n- BillingErrorCode: expected \"Expired\" but found \"Unknown\"";
        var result = Failed(new VstestOutputParser.FailedTestDetail(
            "Ns.Tests.ProcessBillingError", error, "at Ns.Tests.ProcessBillingError()", "1 found, deleted"));
        const string Output = "Test Run Failed.\nBuild FAILED.\n    0 Warning(s)\n    0 Error(s)";

        var markdown = VstestOutputParser.BuildTrxMarkdownReport(result, 1, Output, null, null, TestOutputReportOptions.Default);

        Assert.Contains("1 Tests Failed", markdown, StringComparison.Ordinal);
        Assert.Contains("MessageType = OrderPaymentRefused", markdown, StringComparison.Ordinal);
        Assert.Contains("BillingErrorCode", markdown, StringComparison.Ordinal);
        Assert.Contains("**StdOut:**", markdown, StringComparison.Ordinal);
        Assert.Contains("1 found, deleted", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("All tests passed", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Trx_report_without_results_shows_the_assertion_window_instead_of_the_MSBuild_footer()
    {
        // A crashed or unreadable run has no TRX failure rows; the console tail is the only diagnostic.
        var output = $"""
            Test Run Failed.
              Failed Display name without dots [1 s]
              Error Message:
               Expected unique-assert-token to be true
              Stack Trace:
                 at Tests.Foo()
              Standard Output Messages:
               {new string('z', 5000)}
            Build FAILED.
                0 Warning(s)
                0 Error(s)
            """;
        var result = new VstestTrxResult(TestProcessOutcome.Failed, null, [], [], false, false,
            "One or more expected VSTest launches did not produce a matching complete TRX report.", null);

        var markdown = VstestOutputParser.BuildTrxMarkdownReport(
            result, 1, output, "FullyQualifiedName~ProcessBillingError", "Name suffix", TestOutputReportOptions.Default);

        Assert.Contains("unique-assert-token", markdown, StringComparison.Ordinal);
        Assert.Contains("did not produce a matching complete TRX report", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Trx_report_for_a_restore_failure_is_not_a_success_or_a_filter_miss()
    {
        const string Output = "Restore target(s) failed.\nBuild FAILED.\n    0 Error(s)";
        var result = new VstestTrxResult(TestProcessOutcome.Failed, null, [], [], false, false, null, null);

        var markdown = VstestOutputParser.BuildTrxMarkdownReport(result, 1, Output, null, null, TestOutputReportOptions.Default);

        Assert.Contains("## Test run failed", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("All tests passed", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Filtered tests passed", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("FullyQualifiedName~Missing")]
    public void Confirmed_zero_matches_are_reported_with_or_without_a_caller_filter(string? filter)
    {
        // Rapid TIA passes no filter to the report because its project headline already shows the selection.
        var result = new VstestTrxResult(TestProcessOutcome.Failed, new(0, 0, 0, 0), [], [], false, true, null, null);

        var markdown = VstestOutputParser.BuildTrxMarkdownReport(result, 0, string.Empty, filter, null, TestOutputReportOptions.Default);

        Assert.Contains("no matching tests", markdown, StringComparison.Ordinal);
        Assert.Contains(filter is null ? "the selected filter" : "`FullyQualifiedName~Missing`", markdown, StringComparison.Ordinal);
    }

    private static VstestTrxResult Failed(VstestOutputParser.FailedTestDetail failure) =>
        new(TestProcessOutcome.Failed, new(1, 0, 1, 0), [failure], [], false, false, null, null);
}
