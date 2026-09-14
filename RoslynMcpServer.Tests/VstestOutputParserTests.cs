using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class VstestOutputParserTests
{
    [Fact]
    public void Parse_ignores_msbuild_failed_to_load_prune_line()
    {
        const string output = """
            Failed to load prune package data from NuGet, please verify restore targets.
            Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 120 ms
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.Empty(result.Failures);
        Assert.True(result.HasRecognizedSummary);
        Assert.Equal(3, result.Summary?.Passed);
    }

    [Fact]
    public void Parse_recognizes_vstest_passed_line_with_fqn()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed BrqMover.Tests.WorkItemUrlParserTests.Parse_Valid [12 ms]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.Single(result.PassedTestNames);
        Assert.Contains("WorkItemUrlParserTests", result.PassedTestNames[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_recognizes_vstest_passed_line_with_minute_second_duration()
    {
        const string output = """
            Test Run Successful.
            Total tests: 1
                 Passed: 1
              Passed Ns.NexwayOrderCompletedNotificationContext_ReceivedNotification_ExternalEventsPublished [1 m 28 s]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.False(result.IsPartialSuccess);
        Assert.Single(result.PassedTestNames);
        Assert.Contains(
            "NexwayOrderCompletedNotificationContext_ReceivedNotification_ExternalEventsPublished",
            result.PassedTestNames[0],
            StringComparison.Ordinal);
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~NexwayOrderCompletedNotificationContext_ReceivedNotification_ExternalEventsPublished",
            output,
            result.PassedTestNames));
        var md = VstestOutputParser.BuildMarkdownReport(
            result,
            0,
            output,
            "FullyQualifiedName~NexwayOrderCompletedNotificationContext_ReceivedNotification_ExternalEventsPublished",
            "Name suffix",
            requireFilterMatch: true);
        Assert.Contains("Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterMatchedAnyTest_method_only_display_matches_roslyn_fqn_needle()
    {
        const string method =
            "AvangateNewOrderNotification_MixedB2cB2bOrder_PurchasingContextSkippedLicenseSubscriptionsSaved";
        var output = $"""
            Test Run Successful.
            Total tests: 1
                 Passed: 1
              Passed {method} [3 m 8 s]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.False(result.IsPartialSuccess);
        Assert.Single(result.PassedTestNames);
        Assert.Equal(method, result.PassedTestNames[0]);

        const string filter =
            "FullyQualifiedName~Kaspersky.Ucp.AvangateNewOrderNotificationTests."
            + "AvangateNewOrderNotification_MixedB2cB2bOrder_PurchasingContextSkippedLicenseSubscriptionsSaved";
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(filter, output, result.PassedTestNames));

        var md = VstestOutputParser.BuildMarkdownReport(
            result,
            0,
            output,
            filter,
            "Roslyn-resolved FQN `Kaspersky.Ucp.AvangateNewOrderNotificationTests."
            + "AvangateNewOrderNotification_MixedB2cB2bOrder_PurchasingContextSkippedLicenseSubscriptionsSaved`",
            requireFilterMatch: true);
        Assert.Contains("Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Agent signal", md, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterMatchedAnyTest_method_only_display_matches_class_method_suffix_needle()
    {
        const string method =
            "AvangateNewOrderNotification_MixedB2cB2bOrder_PurchasingContextSkippedLicenseSubscriptionsSaved";
        var output = $"""
            Test Run Successful.
            Total tests: 1
                 Passed: 1
            Passed {method} [3 m 8 s]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~.AvangateNewOrderNotificationTests." + method,
            output,
            result.PassedTestNames));
    }

    [Fact]
    public void FilterMatchedAnyTest_passed_theory_args_match_fqn_needle()
    {
        const string output = """
            Test Run Successful.
            Total tests: 6
                 Passed: 6
              Passed Ns.Billing.OrderTests.TheoryCase(kind: "b2b") [12 ms]
              Passed Ns.Billing.OrderTests.TheoryCase(kind: "b2c") [1 s]
              Passed Ns.Billing.OrderTests.TheoryCase(kind: "mixed") [1 m 28 s]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.Equal(3, result.PassedTestNames.Count);
        Assert.All(result.PassedTestNames, n => Assert.Equal("Ns.Billing.OrderTests.TheoryCase", n));
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~Ns.Billing.OrderTests.TheoryCase",
            output,
            result.PassedTestNames));
        var md = VstestOutputParser.BuildMarkdownReport(
            result,
            0,
            output,
            "FullyQualifiedName~Ns.Billing.OrderTests.TheoryCase",
            "Roslyn-resolved FQN `Ns.Billing.OrderTests.TheoryCase`",
            requireFilterMatch: true);
        Assert.Contains("Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterMatchedAnyTest_does_not_match_other_method_with_shared_suffix()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Ns.Tests.OtherMethod [1 ms]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.False(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~Ns.Tests.Method",
            output,
            result.PassedTestNames));
    }

    [Fact]
    public void Parse_recognizes_vstest_passed_line_with_second_duration()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Ns.SlowTests.TakesOneSecond [1 s]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.Single(result.PassedTestNames);
        Assert.Equal("Ns.SlowTests.TakesOneSecond", result.PassedTestNames[0]);
    }

    [Fact]
    public void Parse_does_not_treat_bracket_noise_as_passed_duration()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Ns.OtherTests.Other [SKIP]
              Passed Ns.OtherTests.Real [12 ms]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.Single(result.PassedTestNames);
        Assert.Equal("Ns.OtherTests.Real", result.PassedTestNames[0]);
    }

    [Fact]
    public void Parse_recognizes_vstest_failed_line_with_minute_second_duration()
    {
        const string output = """
            Total tests: 1
                 Failed: 1
              Failed Ns.SlowTests.FailsAfterMinute [1 m 28 s]
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Single(result.Failures);
        Assert.Equal("Ns.SlowTests.FailsAfterMinute", result.Failures[0].Name);
    }

    [Fact]
    public void Parse_partial_when_exit_zero_without_summary()
    {
        const string output = "Building test projects...\nDone.\n";
        var result = VstestOutputParser.Parse(output, 0);
        Assert.True(result.IsPartialSuccess);
        Assert.False(result.HasRecognizedSummary);
    }

    [Fact]
    public void FilterMatchedAnyTest_false_when_no_test_line_matches_class()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Other.Namespace.OtherTests.Other [1 ms]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.False(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~WorkItemUrlParserTests",
            output,
            result.PassedTestNames));
    }

    [Fact]
    public void Parse_vstest_console_total_tests_and_passed_without_failed_line()
    {
        const string output = """
            Test Run Successful.
            Total tests: 4
                 Passed: 4
              Passed BrqMover.Tests.WorkItemUrlParserTests.A [17 ms]
            """;
        var result = VstestOutputParser.Parse(output, 0);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        Assert.Equal(4, result.Summary?.Total);
        Assert.Equal(4, result.Summary?.Passed);
        Assert.Equal(0, result.Summary?.Failed);
    }

    [Fact]
    public void Parse_slnx_total_tests_and_failed_without_passed_line()
    {
        const string output = """
            Test Run Failed.
            Total tests: 1
                 Failed: 1
             Total time: 1,7379 Minutes
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        Assert.Equal(1, result.Summary?.Total);
        Assert.Equal(0, result.Summary?.Passed);
        Assert.Equal(1, result.Summary?.Failed);

        var md = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, false);
        Assert.Contains("1 Tests Failed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("**Status:** partial", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_infers_passed_from_total_minus_failed_and_skipped()
    {
        const string output = """
            Total tests: 5
                 Failed: 2
                Skipped: 1
             Total time: 2.1 Seconds
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Equal(5, result.Summary?.Total);
        Assert.Equal(2, result.Summary?.Passed);
        Assert.Equal(2, result.Summary?.Failed);
        Assert.Equal(1, result.Summary?.Skipped);
    }

    [Fact]
    public void Parse_total_tests_only_does_not_infer_all_passed()
    {
        const string output = """
            Total tests: 4
             Total time: 1.0 Seconds
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.True(result.HasRecognizedSummary);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void DeduplicateNuGetAuditLines_removes_repeated_warning()
    {
        const string line = "warning NU1904: Package 'X' has a known vulnerability";
        var text = line + "\n" + line + "\nDone.";
        var deduped = VstestOutputParser.DeduplicateNuGetAuditLines(text);
        Assert.Equal(2, deduped.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void BuildMarkdownReport_includes_partial_status()
    {
        var parse = VstestOutputParser.Parse("noise only", 0);
        var md = VstestOutputParser.BuildMarkdownReport(parse, 0, "noise only", null, null, false);
        Assert.Contains("**Status:** partial", md, StringComparison.Ordinal);
        Assert.Contains("Tests completed (exit 0)", md, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_no_matching_filter_emits_agent_signal()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Other.Namespace.OtherTests.Other [1 ms]
            """;
        var parse = VstestOutputParser.Parse(output, 0);
        var md = VstestOutputParser.BuildMarkdownReport(
            parse,
            0,
            output,
            "FullyQualifiedName~.MissingTests.MissingMethod",
            "Name suffix `.MissingTests.MissingMethod`",
            requireFilterMatch: true);

        Assert.Contains("## Filtered test run — no matching tests", md, StringComparison.Ordinal);
        Assert.Contains("**Agent signal:**", md, StringComparison.Ordinal);
        Assert.Contains("**Match mode:** Name suffix", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_keeps_multiline_assertion_beyond_512_chars()
    {
        var padding = new string('x', 700);
        var output = $$"""
            Test Run Failed.
            Total tests: 1
                 Failed: 1
              Failed Ns.NexwayOrderPaymentRefusedNotificationContextTests.ProcessBillingError [1 s]
              Error Message:
               Expected notification to be equivalent to
               {
                   EventKey = Idcd3cab8d-aaaa-bbbb-cccc-dddddddddddd,
                   MessageType = OrderPaymentRefused,
                   {{padding}}
               }
               The following member(s) don't match:
               - BillingErrorCode: expected "Expired" but found "Unknown"
              Stack Trace:
                 at Ns.Tests.ProcessBillingError()
              Standard Output Messages:
               1 found, deleted
            Build FAILED.
                0 Warning(s)
                0 Error(s)

            Time Elapsed 00:00:12.34
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(VstestOutputParser.IsSilentUnparsedFailure(result, output));
        Assert.Single(result.Failures);
        Assert.Contains("MessageType = OrderPaymentRefused", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.Contains("BillingErrorCode", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.Contains('\n', result.Failures[0].Error);
        Assert.DoesNotContain("1 found, deleted", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.DoesNotContain("1 found, deleted", result.Failures[0].Stack, StringComparison.Ordinal);

        var md = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, false);
        Assert.Contains("1 Tests Failed", md, StringComparison.Ordinal);
        Assert.Contains("BillingErrorCode", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", md, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_error_body_head_and_tail_preserve_fluent_diff()
    {
        var padding = new string('y', 4000);
        var output = $$"""
            Total tests: 1
                 Failed: 1
              Failed Ns.SlowTests.BeEquivalentToHuge [12 ms]
              Error Message:
               Expected subject to be equivalent to
               { EventKey = head-token-aa, MessageType = OrderPaymentRefused }
               {{padding}}
               Difference: tail-token-zz BillingErrorCode mismatch
              Stack Trace:
                 at Ns.SlowTests.BeEquivalentToHuge()
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Contains("head-token-aa", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.Contains("tail-token-zz", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.Contains("MIDDLE LOG TRUNCATED", result.Failures[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_failed_line_with_MsBuild_in_test_name_is_not_noise()
    {
        const string output = """
            Total tests: 1
                 Failed: 1
              Failed Ns.TruncatedProcessLogTests.StripTrailingMsBuildOutcome_removes_footer [8 ms]
              Error Message:
               boom
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Single(result.Failures);
        Assert.Contains("StripTrailingMsBuildOutcome_removes_footer", result.Failures[0].Name, StringComparison.Ordinal);
        Assert.Contains("boom", result.Failures[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_recognizes_vstest_failed_theory_line_with_arguments()
    {
        const string output = """
            Total tests: 1
                 Failed: 1
              Failed Ns.SlowTests.TheoryCase(foo: "bar", n: 2) [12 ms]
              Error Message:
               Expected 1 to be 2
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Single(result.Failures);
        Assert.Equal("Ns.SlowTests.TheoryCase", result.Failures[0].Name);
        Assert.Contains("Expected 1 to be 2", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(
            "FullyQualifiedName~Ns.SlowTests.TheoryCase",
            output,
            result.PassedTestNames));
    }

    [Fact]
    public void BuildMarkdownReport_unparsed_failure_shows_error_message_not_msbuild_footer()
    {
        var stdout = new string('z', 5000);
        var output = $"""
            Test Run Failed.
            Total tests: 1
                 Failed: 1
              Failed Display name without dots [1 s]
              Error Message:
               Expected unique-assert-token to be true
              Stack Trace:
                 at Tests.Foo()
              Standard Output Messages:
               {stdout}
            Build FAILED.
                0 Warning(s)
                0 Error(s)
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Single(result.Failures);
        Assert.Equal("Display name without dots", result.Failures[0].Name);
        Assert.Contains("unique-assert-token", result.Failures[0].Error, StringComparison.Ordinal);
        Assert.Equal(1, result.Summary?.Failed);
        Assert.False(VstestOutputParser.IsSilentUnparsedFailure(result, output));

        var md = VstestOutputParser.BuildMarkdownReport(
            result,
            1,
            output,
            "FullyQualifiedName~ProcessBillingError",
            "Name suffix",
            requireFilterMatch: false);
        Assert.Contains("unique-assert-token", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Build FAILED", md, StringComparison.Ordinal);
        Assert.DoesNotContain("0 Error(s)", md, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSilentUnparsedFailure_true_for_empty_or_restore_without_tests()
    {
        var parse = VstestOutputParser.Parse("", exitCode: 1);
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(parse, ""));
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(
            parse,
            "Restore target(s) failed.\nBuild FAILED.\n    0 Error(s)"));
    }
}
