using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class VstestOutputParserTests
{
    [Fact]
    public void BuildMarkdownReport_pairs_console_details_with_adapter_notices_from_stderr()
    {
        const string output = """
              Failed Demo.Tests.Check(input: "private-case-first", value: 2) [1 ms]
              Error Message:
               Identical test failure
              Stack Trace:
                 at Demo.Tests.Check(String input, Int32 value)
              Failed Demo.Tests.Check(input: "private-case-second", value: 1) [< 1 ms]
              Error Message:
               Identical test failure
              Stack Trace:
                 at Demo.Tests.Check(String input, Int32 value)

            Total tests: 2
                 Failed: 2
             Total time: 0.5237 Seconds
            Build FAILED.
            [xUnit.net 00:00:00.24]     Demo.Tests.Check(input: "private-case-first", value: 2) [FAIL]
            [xUnit.net 00:00:00.24]     Demo.Tests.Check(input: "private-case-second", value: 1) [FAIL]
            Test Run Failed.
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        var report = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, requireFilterMatch: false);

        Assert.Equal(new VstestOutputParser.TestSummary(2, 0, 2, 0), result.Summary);
        Assert.Equal(["Identical test failure", "Identical test failure"], result.Failures.Select(f => f.Error));
        Assert.All(result.Failures, failure =>
        {
            Assert.Contains("at Demo.Tests.Check(String input, Int32 value)", failure.Stack, StringComparison.Ordinal);
            Assert.DoesNotContain("Failed Demo.Tests.Check", failure.Stack, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("private-case-", report, StringComparison.Ordinal);
        Assert.Contains("1. **TestName:** `Demo.Tests.Check` (case 1)", report, StringComparison.Ordinal);
        Assert.Contains("2. **TestName:** `Demo.Tests.Check` (case 2)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_keeps_one_failure_when_a_matching_notice_survives_capture()
    {
        var output = $"""
            {TruncatedProcessLog.CaptureMarker}
            Total tests: 1
                 Failed: 1
              Failed Demo.Tests.Check(input: "private-case-token", value: 2) [< 1 ms]
              Error Message:
               Failure detail
              Stack Trace:
                 at Demo.Tests.Check(String input, Int32 value)
            [xUnit.net 00:00:00.24]     Demo.Tests.Check(input: "private-case-token", value: 2) [FAIL]
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1, summaryTruncated: false, outputTruncated: true);
        var report = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, requireFilterMatch: false);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Demo.Tests.Check", failure.Name);
        Assert.Equal("Failure detail", failure.Error);
        Assert.Equal(new VstestOutputParser.TestSummary(1, 0, 1, 0), result.Summary);
        Assert.Contains("1. **TestName:** `Demo.Tests.Check`" + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.DoesNotContain("private-case-token", report, StringComparison.Ordinal);
        Assert.DoesNotContain("2. **TestName:**", report, StringComparison.Ordinal);
        Assert.DoesNotContain(TruncatedProcessLog.CaptureMarker, report, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing first 5 failures only", report, StringComparison.Ordinal);
    }

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
            "FullyQualifiedName~private_project.AvangateNewOrderNotificationTests."
            + "AvangateNewOrderNotification_MixedB2cB2bOrder_PurchasingContextSkippedLicenseSubscriptionsSaved";
        Assert.True(VstestOutputParser.FilterMatchedAnyTest(filter, output, result.PassedTestNames));

        var md = VstestOutputParser.BuildMarkdownReport(
            result,
            0,
            output,
            filter,
            "Roslyn-resolved FQN `private_project.AvangateNewOrderNotificationTests."
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
    public void BuildMarkdownReport_lists_methods_separately_and_counts_each_theory_case()
    {
        const string output = """
            Test Run Successful.
            Total tests: 3
                 Passed: 3
              Passed Demo.Samples0.InRange(value: 0) [1 ms]
              Passed Demo.Samples0.InRange(value: 1) [< 1 ms]
              Passed Demo.Samples0.Creates`Provider [< 1 ms]
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.Equal(
            ["Demo.Samples0.InRange", "Demo.Samples0.InRange", "Demo.Samples0.Creates`Provider"],
            result.PassedTestNames);

        var report = VstestOutputParser.BuildMarkdownReport(
            result,
            exitCode: 0,
            output,
            "FullyQualifiedName~Demo.Samples0",
            filterDescription: null,
            requireFilterMatch: true);

        Assert.Contains("Total: **3** · Passed: **3** · Failed: **0**", report, StringComparison.Ordinal);
        Assert.Contains(
            "Matched test methods (preview): `Demo.Samples0.InRange`, `Demo.Samples0.Creates'Provider`",
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_takes_the_first_five_distinct_method_names()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7
              Passed Test.Matrix.First(value: 1) [1 ms]
              Passed Test.Matrix.First(value: 2) [1 ms]
              Passed Test.Matrix.Second [1 ms]
              Passed Test.Matrix.Third [1 ms]
              Passed Test.Matrix.Fourth [1 ms]
              Passed Test.Matrix.Fifth [1 ms]
              Passed Test.Matrix.Sixth [1 ms]
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        var report = VstestOutputParser.BuildMarkdownReport(
            result,
            exitCode: 0,
            output,
            "FullyQualifiedName~Test.Matrix",
            filterDescription: null,
            requireFilterMatch: true);

        Assert.Contains(
            "Matched test methods (preview): `Test.Matrix.First`, `Test.Matrix.Second`, `Test.Matrix.Third`, `Test.Matrix.Fourth`, `Test.Matrix.Fifth`",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Test.Matrix.Sixth", report, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("[1 m 28 s]")]
    [InlineData("[< 1 ms]")]
    public void Parse_recognizes_vstest_failed_line_with_duration(string duration)
    {
        var output = $"""
            Total tests: 1
                 Failed: 1
              Failed Ns.ExampleTests.Fails {duration}
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Single(result.Failures);
        Assert.Equal("Ns.ExampleTests.Fails", result.Failures[0].Name);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void BuildMarkdownReport_caps_failures_after_pairing_adapter_notices_with_theory_rows(int failureCount)
    {
        var cases = Enumerable.Range(1, failureCount);
        var notices = cases.Select(value => $"[xUnit.net 00:00:01.00]     Demo.Tests.Check(value: {value}) [FAIL]");
        var detailRows = cases.Select(value => $"""
              Failed Demo.Tests.Check(value: {value}) [< 1 ms]
              Error Message:
               Failure detail {value}
            """);
        var failureLines = string.Join('\n', notices.Concat(detailRows));
        var output = $"Total tests: {failureCount}\n     Failed: {failureCount}\n{failureLines}";

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        var report = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, requireFilterMatch: false);

        Assert.Equal(failureCount, result.Summary?.Failed);
        Assert.Equal(Enumerable.Range(1, 5).Select(value => $"Failure detail {value}"), result.Failures.Select(f => f.Error));
        Assert.Equal(failureCount > 5, report.Contains("Showing first 5 failures only", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_keeps_adapter_notice_for_a_different_case_of_the_same_theory()
    {
        const string output = """
            Total tests: 2
                 Failed: 2
            [xUnit.net 00:00:01.00]     Demo.Tests.Check(value: 1) [FAIL]
              Error Message:
               Adapter-only detail
            [xUnit.net 00:00:01.00]     Demo.Tests.Check(value: 2) [FAIL]
              Failed Demo.Tests.Check(value: 2) [< 1 ms]
              Error Message:
               Console detail
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);

        Assert.Equal(2, result.Summary?.Failed);
        Assert.Equal(["Adapter-only detail", "Console detail"], result.Failures.Select(f => f.Error));
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
    public void Passing_test_with_an_unverified_display_name_keeps_filter_uncertainty()
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

        Assert.Contains("**Status:** partial", md, StringComparison.Ordinal);
        Assert.Contains("Retained test names do not verify the filter match.", md, StringComparison.Ordinal);
        Assert.Contains("Reported totals: 1 total, 1 passed, 0 failed, 0 skipped.", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
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
        Assert.Contains("1 found, deleted", result.Failures[0].StdOut, StringComparison.Ordinal);

        var md = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, false);
        Assert.Contains("1 Tests Failed", md, StringComparison.Ordinal);
        Assert.Contains("BillingErrorCode", md, StringComparison.Ordinal);
        Assert.Contains("1 found, deleted", md, StringComparison.Ordinal);
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
        Assert.Contains("**StdOut:**", md, StringComparison.Ordinal);
        Assert.Contains("includeFullOutput=true", md, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_keeps_conversation_id_in_stdout_head()
    {
        const string conversation = "Test. Start time 2026-09-17T09:06:32Z. ConversationId: abc-123-guid";
        const string middle = "MIDDLE-TOKEN-SHOULD-DROP";
        var paddingHead = new string('x', 2000);
        var paddingTail = new string('y', 2000);
        var output = $"""
            Total tests: 1
                 Failed: 1
              Failed Ns.Pay.WhenIp [1 s]
              Error Message:
               Expected ip to be ""
              Stack Trace:
                 at Ns.Pay.WhenIp()
              Standard Output Messages:
               {conversation}
               {paddingHead}
               {middle}
               {paddingTail}
               tail-end-zzz
              Standard Error Messages:
               stderr-boom
            """;

        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Contains(conversation, result.Failures[0].StdOut, StringComparison.Ordinal);
        Assert.Contains(middle, result.Failures[0].StdOut, StringComparison.Ordinal);
        Assert.Contains("stderr-boom", result.Failures[0].StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(conversation, result.Failures[0].Error, StringComparison.Ordinal);

        var md = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, false);
        Assert.Contains(conversation, md, StringComparison.Ordinal);
        Assert.Contains("tail-end-zzz", md, StringComparison.Ordinal);
        Assert.DoesNotContain(middle, md, StringComparison.Ordinal);
        Assert.Contains("stderr-boom", md, StringComparison.Ordinal);
        Assert.Contains("includeFullOutput=true", md, StringComparison.Ordinal);

        var full = VstestOutputParser.BuildMarkdownReport(
            result,
            1,
            output,
            null,
            null,
            false,
            new TestOutputReportOptions(IncludeFullOutput: true, MaxOutputChars: 0));
        Assert.Contains(middle, full, StringComparison.Ordinal);
        Assert.DoesNotContain("includeFullOutput=true", full, StringComparison.Ordinal);

        var raised = VstestOutputParser.BuildMarkdownReport(
            result,
            1,
            output,
            null,
            null,
            false,
            new TestOutputReportOptions(IncludeFullOutput: false, MaxOutputChars: 12_000));
        Assert.Contains(middle, raised, StringComparison.Ordinal);
    }

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
    public void IsSilentUnparsedFailure_true_for_empty_or_restore_without_tests()
    {
        var parse = VstestOutputParser.Parse("", exitCode: 1);
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(parse, ""));
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(
            parse,
            "Restore target(s) failed.\nBuild FAILED.\n    0 Error(s)"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Filtered_report_keeps_a_confirmed_failure_when_capture_loses_the_test_name(int exitCode)
    {
        const string Output = "Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1";
        const string Filter = "FullyQualifiedName~Fixture.Match";
        var parse = VstestOutputParser.Parse(Output, exitCode, summaryTruncated: false, outputTruncated: true);
        var report = VstestOutputParser.BuildMarkdownReport(parse, exitCode, Output, Filter, null, requireFilterMatch: true);

        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("1 total, 0 passed, 1 failed, 0 skipped", report, StringComparison.Ordinal);
        Assert.Contains("The captured output does not identify which test failed.", report, StringComparison.Ordinal);
        var filterLine = Assert.Single(
            report.Split(Environment.NewLine),
            line => line.Contains("Fixture.Match", StringComparison.Ordinal));
        Assert.Equal($"**Filter:** `{Filter}`", filterLine);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Filtered_report_with_passing_totals_and_unknown_match_stays_partial(int exitCode, bool outputTruncated)
    {
        const string Output = """
            Test run for bin/net11.0/Fixture.dll (.NETCoreApp,Version=v11.0)
            A total of 1 test files matched the specified pattern.

            Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 8 ms - Fixture.dll (net11.0)
            """;
        var parse = VstestOutputParser.Parse(Output, exitCode, summaryTruncated: false, outputTruncated);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse,
            exitCode,
            Output,
            "FullyQualifiedName~Fixture.Match",
            null,
            requireFilterMatch: true,
            default,
            out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("Retained test names do not verify the filter match.", report, StringComparison.Ordinal);
        Assert.Contains("Reported totals: 1 total, 1 passed, 0 failed, 0 skipped.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## Test run failed", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Probe: No test matches the given testcase filter `FullyQualifiedName~Fixture.Match` in bin/Fixture.dll")]
    [InlineData("No test matches the given testcase filter is a VSTest message")]
    [InlineData("No test matches the given testcase filter in Unrelated.dll")]
    public void Unattributed_no_match_text_does_not_verify_a_filter_with_passing_totals(string notice)
    {
        var output = $"""
            Test run for bin/Fixture.dll (.NETCoreApp,Version=v11.0)
            {notice}
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Fixture.dll (net11.0)
            """;
        var parse = VstestOutputParser.Parse(output, 0);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 0, output, "FullyQualifiedName~Fixture.Match", null, requireFilterMatch: true, default, out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("Retained test names do not verify the filter match.", report, StringComparison.Ordinal);
        Assert.Contains("Reported totals: 1 total, 1 passed, 0 failed, 0 skipped.", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  Failed Demo.Fixture.Match [1 ms]", "The active test run was aborted. Reason: Test host process crashed")]
    [InlineData("[xUnit.net 00:00:00.27] Demo.Fixture.Match [FAIL]", "Test Run Aborted.")]
    [InlineData("  Failed Demo.Fixture.Match [1 ms]", "")]
    [InlineData("", "Test Run Aborted.")]
    [InlineData("", "Test Run Failed.")]
    public void Earlier_passing_totals_cannot_hide_a_later_failure(string failureLine, string runFooter)
    {
        var output = $"Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n{failureLine}\n{runFooter}";
        var parse = VstestOutputParser.Parse(output, exitCode: 1, summaryTruncated: false, outputTruncated: true);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse,
            1,
            output,
            "FullyQualifiedName~Demo.Fixture.Match",
            null,
            requireFilterMatch: true,
            default,
            out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Filtered tests passed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("**Status:** partial", report, StringComparison.Ordinal);
        if (failureLine.Length > 0)
        {
            Assert.Contains("**TestName:** `Demo.Fixture.Match`", report, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Retained test names do not verify the filter match", report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Passing_totals_with_an_unexplained_nonzero_exit_do_not_confirm_success()
    {
        const string Output = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n  Passed Demo.Fixture.Match [1 ms]";
        var parse = VstestOutputParser.Parse(Output, exitCode: 1);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 1, Output, "FullyQualifiedName~Demo.Fixture.Match", null, true, default, out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("sibling test projects", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MSB4181", false)]
    [InlineData("MSB4181", true)]
    [InlineData("CI-001", false)]
    [InlineData("CI-001", true)]
    [InlineData("quality.gate / storage", true)]
    public async Task Sibling_no_match_does_not_hide_an_independent_msbuild_error(string code, bool truncate)
    {
        var error = $"1>Demo.csproj(21,5): error {code}: Test result directory could not be written.";
        var noise = truncate ? string.Concat(Enumerable.Repeat(new string('x', 1023) + "\n", 150)) : string.Empty;
        var output = $"""
              Passed Demo.Fixture.Match [1 ms]
            Total tests: 1
                Passed: 1
            No test matches the given testcase filter in Sibling.dll
            {noise}
            {error}
            {noise}
            """;
        using var reader = new StringReader(output);
        var capture = new BoundedProcessOutput(reader);
        await capture.Completion;
        var retained = capture.Finish();
        Assert.Equal(truncate, retained.Truncated);
        var parse = VstestOutputParser.Parse(retained.Text, 1, retained.SummaryTruncated, retained.Truncated);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 1, retained.Text, "FullyQualifiedName~Demo.Fixture.Match", null, true, default, out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.Contains("**MSBuild error:** " + error, report, StringComparison.Ordinal);
        Assert.Contains("1 total, 1 passed, 0 failed", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"1>Build validation : error CI-001: Result directory could not be written. [E:\work\Example.proj]")]
    [InlineData(@"error Gate-Code: Result directory could not be written. [E:\work\Example.csproj::TargetFramework=net11.0]")]
    public void Project_context_identifies_custom_errors_without_source_coordinates(string error)
    {
        var output = $"""
              Passed Demo.Fixture.Match [1 ms]
            Total tests: 1
                Passed: 1
            No test matches the given testcase filter in Sibling.dll
            Expected: error connecting to server: retrying
            {error}
            """;

        var parse = VstestOutputParser.Parse(output, 1);
        Assert.Equal(error, parse.BuildError);
        Assert.True(parse.HasConfirmedFailure);
    }

    [Theory]
    [InlineData(1, "1>Demo.csproj(21,5): warning W0001: an example of error MSB4181:")]
    [InlineData(1, "A test printed error MSB4181: in its explanation.")]
    [InlineData(1, "The test printed: error CI-001: example diagnostic")]
    [InlineData(1, "Expected: error connecting to server: retrying")]
    [InlineData(1, "  Passed Demo.Fixture.Theory(value: error MSB4181: example) [1 ms]")]
    [InlineData(1, "  Skipped Demo.Fixture.Example: error MSB4181: expected [1 ms]")]
    [InlineData(1, "[xUnit.net 00:00:00.15]     Demo.Fixture.Example: error MSB4181: expected [SKIP]")]
    [InlineData(1, "No test matches the given testcase filter `DisplayName=Example: error MSB4181: expected` in Sibling.dll")]
    [InlineData(1, "  Passed Demo.Fixture.Theory(value: error CI-001: example) [1 ms]")]
    [InlineData(0, "error MSB4181: sample test output")]
    public void Test_output_is_not_an_msbuild_diagnostic_or_a_reason_to_ignore_the_exit_code(int exitCode, string line)
    {
        var output = $"""
              Passed Demo.Fixture.Match [1 ms]
            Total tests: 2
                Passed: 1
                Skipped: 1
            No test matches the given testcase filter in Sibling.dll
            {line}
            """;
        var parse = VstestOutputParser.Parse(output, exitCode);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, exitCode, output, "FullyQualifiedName~Demo.Fixture.Match", null, true, default, out var succeeded);

        Assert.Equal(exitCode == 0, succeeded);
        Assert.Contains(exitCode == 0 ? "## Filtered tests passed" : "## Test run failed", report, StringComparison.Ordinal);
        Assert.Contains("Skipped: **1**", report, StringComparison.Ordinal);
        Assert.DoesNotContain("**MSBuild error:**", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Total tests: 1", false)]
    [InlineData("A test printed Total tests: 1", true)]
    public void A_failed_counter_in_a_distinct_summary_block_overrides_sibling_no_match(string header, bool expectedSuccess)
    {
        var output = $"""
            No test matches the given testcase filter in Sibling.dll
              Passed Demo.Fixture.Match [1 ms]
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
            {header}
                 Failed: 1
            """;
        var parse = VstestOutputParser.Parse(output, 0, summaryTruncated: false, outputTruncated: true);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 0, output, "FullyQualifiedName~Demo.Fixture.Match", null, true, default, out var succeeded);

        Assert.Equal(expectedSuccess, succeeded);
        Assert.Contains(expectedSuccess ? "## Filtered tests passed" : "## Test run failed", report, StringComparison.Ordinal);
        if (expectedSuccess)
        {
            AssertBalancedSummary(parse.Summary, total: 1, passed: 1, failed: 0, skipped: 0);
        }
        else
        {
            Assert.Null(parse.Summary);
            Assert.Contains("Confirmed lower bounds: 1 passed, 1 failed", report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Passing_tests_and_a_sibling_no_match_do_not_explain_the_command_exit_code()
    {
        const string output = """
            No test matches the given testcase filter `FullyQualifiedName~MyNamespace.MyClass.MyTest` in C:\x\B\bin\Debug\net10.0\B.dll

              Passed MyNamespace.MyClass.MyTest [15 ms]

            Test Run Successful.
            Total tests: 1
                 Passed: 1
             Total time: 1,0813 Seconds
            """;
        var parse = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Equal(1, parse.Summary?.Total);
        Assert.Equal(1, parse.Summary?.Passed);
        Assert.Equal(0, parse.Summary?.Failed);
        Assert.False(VstestOutputParser.IsSilentUnparsedFailure(parse, output));

        var md = VstestOutputParser.BuildMarkdownReport(
            parse,
            1,
            output,
            "FullyQualifiedName~MyNamespace.MyClass.MyTest",
            "Name suffix",
            requireFilterMatch: true,
            default,
            out var succeeded);

        Assert.False(succeeded);
        Assert.Contains("## Test run failed", md, StringComparison.Ordinal);
        Assert.Contains("Total: **1** · Passed: **1** · Failed: **0**", md, StringComparison.Ordinal);
        Assert.Contains("exited with code `1`", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
        Assert.DoesNotContain("## Filtered tests passed", md, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_mstest_multi_project_matching_test_not_flagged_no_match()
    {
        const string output = """
            No test matches the given testcase filter `FullyQualifiedName~Recovery.FinalStateTests.Test_Foo` in C:\src\bin\Debug\net8.0\WiWorkflowTests.dll
            No test matches the given testcase filter `FullyQualifiedName~Recovery.FinalStateTests.Test_Foo` in C:\src\bin\Debug\net8.0\RegexTests.dll

              Passed Test_Foo [28 ms]

            Test Run Successful.
            Total tests: 1
                 Passed: 1
             Total time: 0,3378 Seconds
            """;
        var parse = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.Equal(1, parse.Summary?.Total);
        Assert.Equal(1, parse.Summary?.Passed);

        var md = VstestOutputParser.BuildMarkdownReport(
            parse,
            0,
            output,
            "FullyQualifiedName~Recovery.FinalStateTests.Test_Foo",
            "Name suffix",
            requireFilterMatch: true);

        Assert.Contains("## Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
        Assert.DoesNotContain("non-zero", md, StringComparison.Ordinal);
        Assert.DoesNotContain("_Exit code is non-zero", md, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void BuildMarkdownReport_all_assemblies_no_match_reports_filtered_no_match(int exitCode, bool includeRunHeaders)
    {
        const string RunHeaders = """
            Test run for C:\src\bin\Debug\net10.0\A.dll (.NETCoreApp,Version=v10.0)
            Test run for C:\src\bin\Debug\net10.0\B.dll (.NETCoreApp,Version=v10.0)
            """;
        const string Notices = """
            No test matches the given testcase filter `FullyQualifiedName~ZZZ_NO_SUCH_TEST_ZZZ` in C:\src\bin\Debug\net10.0\A.dll
            No test matches the given testcase filter `FullyQualifiedName~ZZZ_NO_SUCH_TEST_ZZZ` in C:\src\bin\Debug\net10.0\B.dll
            """;
        var output = includeRunHeaders ? RunHeaders + "\n" + Notices : Notices;
        var parse = VstestOutputParser.Parse(output, exitCode);
        Assert.Null(parse.Summary);

        var md = VstestOutputParser.BuildMarkdownReport(
            parse,
            exitCode,
            output,
            "FullyQualifiedName~ZZZ_NO_SUCH_TEST_ZZZ",
            "Name suffix",
            requireFilterMatch: true);

        Assert.Contains("## Filtered test run — no matching tests", md, StringComparison.Ordinal);
        Assert.DoesNotContain("All tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Filtered tests passed", md, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FullyQualifiedName~Demo.Fixture.Other", false, false)]
    [InlineData("FullyQualifiedName~Demo.Fixture.Other", true, false)]
    [InlineData(" FullyQualifiedName~Demo.Fixture.Match ", false, true)]
    [InlineData(" FullyQualifiedName~Demo.Fixture.Match ", true, true)]
    public void A_full_notice_must_quote_the_requested_filter_to_confirm_no_match(string effectiveFilter, bool includeZeroTotals, bool expectedNoMatch)
    {
        var output = $"No test matches the given testcase filter `{effectiveFilter}` in Fixture.dll.";
        if (includeZeroTotals)
        {
            output += "\nPassed! - Failed: 0, Passed: 0, Skipped: 0, Total: 0";
        }

        var parse = VstestOutputParser.Parse(output, 0);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 0, output, "FullyQualifiedName~Demo.Fixture.Match", null, requireFilterMatch: true);

        Assert.Equal(expectedNoMatch, report.Contains("no matching tests", StringComparison.Ordinal));
        Assert.Equal(!expectedNoMatch, report.Contains("**Status:** partial", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_sibling_notice_does_not_cover_a_started_source_with_no_result(int exitCode)
    {
        const string Output = """
            Test run for bin/Fixture.dll (.NETCoreApp,Version=v11.0)
            Test run for bin/Sibling.dll (.NETCoreApp,Version=v11.0)
            No test matches the given testcase filter `FullyQualifiedName~Demo.Fixture.Match` in bin/Sibling.dll.
            """;
        var parse = VstestOutputParser.Parse(Output, exitCode);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, exitCode, Output, "FullyQualifiedName~Demo.Fixture.Match", null, requireFilterMatch: true);

        Assert.Null(parse.Summary);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("The captured output does not establish a no-match for the complete run.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", report, StringComparison.Ordinal);
    }

    [Fact]
    public void A_notice_for_the_source_of_passing_totals_does_not_close_a_different_start()
    {
        const string Output = """
            Test run for bin/Fixture.dll (.NETCoreApp,Version=v11.0)
            Test run for bin/Sibling.dll (.NETCoreApp,Version=v11.0)
            No test matches the given testcase filter `FullyQualifiedName~Demo.Fixture.Match` in bin/Fixture.dll.
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Fixture.dll (net11.0)
            """;
        var parse = VstestOutputParser.Parse(Output, 0);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 0, Output, "FullyQualifiedName~Demo.Fixture.Match", null, requireFilterMatch: true);

        Assert.Null(parse.Summary);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.Contains("Confirmed lower bounds: 1 passed", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Skipped")]
    public void Sibling_no_match_with_a_case_outcome_and_no_summary_keeps_filter_uncertainty(string outcome)
    {
        var output = $"""
            No test matches the given testcase filter in Sibling.dll
              {outcome} Demo.Fixture.Match [1 ms]
            """;
        var parse = VstestOutputParser.Parse(output, 0);
        var report = VstestOutputParser.BuildMarkdownReport(
            parse, 0, output, "FullyQualifiedName~Demo.Fixture.Match", null, requireFilterMatch: true);

        Assert.Null(parse.Summary);
        Assert.Contains("**Status:** partial", report, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", report, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_restore_failed_without_summary_is_not_success()
    {
        const string output = """
            Restore target(s) failed.
            Build FAILED.
                0 Error(s)
            """;
        var parse = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Null(parse.Summary);
        Assert.True(VstestOutputParser.IsSilentUnparsedFailure(parse, output));

        var md = VstestOutputParser.BuildMarkdownReport(parse, 1, output, null, null, requireFilterMatch: false);
        Assert.DoesNotContain("All tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_real_failure_keeps_assertion_details()
    {
        const string output = """
            Test Run Failed.
            Total tests: 1
                 Failed: 1
              Failed Ns.SlowTests.BeEquivalentTo [12 ms]
              Error Message:
               Expected 1 to be 2
              Stack Trace:
                 at Ns.SlowTests.BeEquivalentTo()
            """;
        var parse = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.Equal(1, parse.Summary?.Failed);
        Assert.False(VstestOutputParser.IsSilentUnparsedFailure(parse, output));

        var md = VstestOutputParser.BuildMarkdownReport(parse, 1, output, null, null, requireFilterMatch: false);
        Assert.Contains("❌", md, StringComparison.Ordinal);
        Assert.Contains("1 Tests Failed", md, StringComparison.Ordinal);
        Assert.Contains("Expected 1 to be 2", md, StringComparison.Ordinal);
        Assert.DoesNotContain("All tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("non-zero", md, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdownReport_single_assembly_success_omits_nonzero_exit_note()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
              Passed Ns.Class.Method [12 ms]
            """;
        var parse = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.Equal(0, parse.Summary?.Failed);
        Assert.Equal(1, parse.Summary?.Total);

        var md = VstestOutputParser.BuildMarkdownReport(parse, 0, output, null, null, requireFilterMatch: false);
        Assert.Contains("## All tests passed successfully!", md, StringComparison.Ordinal);
        Assert.DoesNotContain("non-zero", md, StringComparison.Ordinal);
        Assert.DoesNotContain("_Exit code is non-zero", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Tests Failed", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_sums_end_summary_lines_across_two_assemblies()
    {
        const string output = """
            Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 120 ms
            Passed!  - Failed:     1, Passed:     4, Skipped:     2, Total:     7, Duration: 800 ms
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        AssertBalancedSummary(result.Summary, total: 10, passed: 7, failed: 1, skipped: 2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Unknown_total_preserves_reported_outcomes_across_targets(bool outputTruncated, bool compactSummary)
    {
        const string Output = """
            Total tests: 1
                 Passed: 1
              Failed Demo.Fixture.Match [1 ms]
            Test Run Aborted.
            Total tests: Unknown
                 Failed: 1
            """;
        var log = compactSummary ? "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1\n" + Output : Output;
        var parsed = VstestOutputParser.Parse(log, 1, summaryTruncated: false, outputTruncated);
        var report = VstestOutputParser.BuildMarkdownReport(
            parsed, 1, log, "Demo.Fixture.Match", null, requireFilterMatch: true, default, out var succeeded);

        Assert.Null(parsed.Summary);
        Assert.False(succeeded);
        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.Contains("Total tests: unknown. Confirmed lower bounds: 1 passed, 1 failed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Total: **", report, StringComparison.Ordinal);
        Assert.True(VstestOutputParser.IsSummaryCaptureLine("Total tests: Unknown"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completed_targets_report_balanced_totals(bool outputTruncated)
    {
        const string Output = """
            Total tests: 1
                Passed: 1
            Total tests: 3
                Failed: 2
                Skipped: 1
            """;
        var parsed = VstestOutputParser.Parse(Output, 1, summaryTruncated: false, outputTruncated);
        AssertBalancedSummary(parsed.Summary, total: 4, passed: 1, failed: 2, skipped: 1);
        var report = VstestOutputParser.BuildMarkdownReport(parsed, 1, Output, null, null, requireFilterMatch: false);
        Assert.Contains("Total: **4** · Passed: **1** · Failed: **2** · Skipped: **1**", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Retained_failed_counter_proves_failure_with_unknown_total_and_missing_test_name(bool outputTruncated, bool summaryTruncated)
    {
        var log = "Total tests: Unknown\n    Failed: 2\n" + new string('x', 4096);
        var parsed = VstestOutputParser.Parse(log, 0, summaryTruncated, outputTruncated);
        var report = VstestOutputParser.BuildMarkdownReport(
            parsed, 0, log, "Demo.Match", null, requireFilterMatch: true, default, out var succeeded);

        Assert.Null(parsed.Summary);
        Assert.False(succeeded);
        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.Contains("Total tests: unknown. Confirmed lower bounds: 2 failed.", report, StringComparison.Ordinal);
        Assert.Contains("does not identify which test failed", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unknown_total_and_a_named_failure_keep_absent_counters_unknown(bool outputTruncated, bool summaryTruncated)
    {
        const string Output = """
              Failed Demo.Fixture.Match [1 ms]
            Test Run Aborted.
            Total tests: Unknown
                Passed: 0
            """;
        var parsed = VstestOutputParser.Parse(Output, 1, summaryTruncated, outputTruncated);
        var report = VstestOutputParser.BuildMarkdownReport(parsed, 1, Output, null, null, requireFilterMatch: false);

        Assert.Contains("## Test run failed", report, StringComparison.Ordinal);
        Assert.Contains("Total tests: unknown. Confirmed lower bounds: 0 passed, 1 failed.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0 failed", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0 skipped", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_headers_in_truncated_capture_do_not_double_count_overlapping_counters()
    {
        const string Output = """
            Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3
                Passed: 3
                Failed: 2
            """;
        var parsed = VstestOutputParser.Parse(Output, 1, summaryTruncated: true, outputTruncated: true);
        var report = VstestOutputParser.BuildMarkdownReport(parsed, 1, Output, null, null, requireFilterMatch: false);

        Assert.Contains("Confirmed lower bounds: 3 passed, 2 failed, 0 skipped.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Total: **", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_sums_total_tests_blocks_across_two_all_pass_assemblies()
    {
        const string output = """
            Test Run Successful.
            Total tests: 10
                 Passed: 10

            Test Run Successful.
            Total tests: 5
                 Passed: 5
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        AssertBalancedSummary(result.Summary, total: 15, passed: 15, failed: 0, skipped: 0);
    }

    [Fact]
    public void Parse_zero_match_assembly_plus_three_passed_aggregates_three()
    {
        const string output = """
            Total tests: 0

            Test Run Successful.
            Total tests: 3
                 Passed: 3
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        AssertBalancedSummary(result.Summary, total: 3, passed: 3, failed: 0, skipped: 0);
    }

    [Fact]
    public void Mixed_formats_without_run_boundaries_report_confirmed_lower_bounds()
    {
        const string output = """
            Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
            Test Run Failed.
            Total tests: 2
                 Failed: 2
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        Assert.Null(result.Summary);
        var report = VstestOutputParser.BuildMarkdownReport(result, 1, output, null, null, requireFilterMatch: false);
        Assert.Contains("Confirmed lower bounds: 5 passed, 2 failed", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Independent_frameworks_add_their_counts_after_bounded_capture(bool compactFirst, bool aborted)
    {
        var firstSummary = compactFirst ? "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1" : "Total tests: 1\n    Passed: 1";
        var noise = string.Concat(Enumerable.Repeat(new string('x', 1023) + "\n", 128));
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            {firstSummary}
            {noise}
            Test run for bin/net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            Test Run Successful.
            Total tests: 3
                Passed: 3
            {noise}
            """;
        if (aborted)
        {
            output += "\nTest Run Aborted.\nTotal tests: Unknown\n    Failed: 1";
        }

        using var reader = new StringReader(output);
        var capture = new BoundedProcessOutput(reader);
        await capture.Completion;
        var retained = capture.Finish();
        Assert.True(retained.Truncated);
        Assert.False(retained.SummaryTruncated);
        var result = VstestOutputParser.Parse(retained.Text, aborted ? 1 : 0, retained.SummaryTruncated, retained.Truncated);
        if (aborted)
        {
            Assert.Null(result.Summary);
            var report = VstestOutputParser.BuildMarkdownReport(result, 1, retained.Text, null, null, requireFilterMatch: false);
            Assert.Contains("Total tests: unknown. Confirmed lower bounds: 4 passed, 1 failed", report, StringComparison.Ordinal);
        }
        else
        {
            AssertBalancedSummary(result.Summary, total: 4, passed: 4, failed: 0, skipped: 0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_formats_for_one_target_count_once_in_either_order(bool compactFirst)
    {
        const string Compact = "Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3";
        const string Numeric = "Test Run Successful.\nTotal tests: 3\n    Passed: 3";
        var summaries = compactFirst ? Compact + "\n" + Numeric : Numeric + "\n" + Compact;
        var result = VstestOutputParser.Parse("Test run for Demo.dll (.NETCoreApp,Version=v11.0)\n" + summaries, 0);

        AssertBalancedSummary(result.Summary, total: 3, passed: 3, failed: 0, skipped: 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_previous_run_can_repeat_its_summary_after_the_next_run_starts(bool compactFirst)
    {
        const string Compact = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1";
        const string Numeric = "Total tests: 1\n    Passed: 1";
        var output = $"""
            Test run for net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            {(compactFirst ? Compact : Numeric)}
            Test run for net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            {(compactFirst ? Numeric : Compact)}
            Total tests: 3
                Passed: 3
            """;

        AssertBalancedSummary(VstestOutputParser.Parse(output, 0).Summary, total: 4, passed: 4, failed: 0, skipped: 0);
    }

    [Theory]
    [InlineData("Demo.dll (net11.0)", 2)]
    [InlineData("Demo.dll (net10.0)", null)]
    public void Equal_counts_use_the_named_framework_and_the_numeric_summary_position(string compactSource, int? total)
    {
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Total tests: 1
                Passed: 1
            Test run for bin/net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - {compactSource}
            """;
        var result = VstestOutputParser.Parse(output, 0);

        Assert.Equal(total, result.Summary?.Total);
        if (total is null)
        {
            Assert.Equal(1, result.IncompleteCounts?.Passed);
        }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    [InlineData(false, 3)]
    public void Sequential_mixed_loggers_count_each_framework(bool compactFirst, int secondCount)
    {
        var output = Run("10.0", 1, compactFirst) + "\n" + Run("11.0", secondCount, !compactFirst);
        var parsed = VstestOutputParser.Parse(output, 0);

        AssertBalancedSummary(parsed.Summary, total: 1 + secondCount, passed: 1 + secondCount, failed: 0, skipped: 0);

        static string Run(string version, int count, bool compact)
        {
            var start = $"Test run for bin/net{version}/Demo.dll (.NETCoreApp,Version=v{version})\n";
            if (compact)
            {
                return start + $"Passed! - Failed: 0, Passed: {count}, Skipped: 0, Total: {count}, Duration: 1 ms - Demo.dll (net{version})";
            }

            var cases = string.Concat(Enumerable.Repeat("  Passed Demo.Fixture.Match [1 ms]\n", count));
            return start + cases + $"Test Run Successful.\nTotal tests: {count}\n     Passed: {count}";
        }
    }

    [Theory]
    [InlineData(false, "", false)]
    [InlineData(false, "  Skipped Demo.Fixture.Match [1 ms]", false)]
    [InlineData(true, "  Passed Demo.Fixture.Match [1 ms]", false)]
    [InlineData(false, "  Passed Demo.Fixture.Match [1 ms]", true)]
    public void Unattributed_verbose_totals_can_still_repeat_a_compact_result(bool overlapping, string detail, bool truncated)
    {
        const string SecondStart = "Test run for bin/net11.0/Demo.dll (.NETCoreApp,Version=v11.0)";
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            {(overlapping ? SecondStart : "")}
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Demo.dll (net10.0)
            {(overlapping ? "" : SecondStart)}
            {detail}
            Test Run Successful.
            Total tests: 1
                 Passed: 1
            """;

        var parsed = VstestOutputParser.Parse(output, 0, summaryTruncated: false, outputTruncated: truncated);

        Assert.Null(parsed.Summary);
        Assert.Equal(1, parsed.IncompleteCounts?.Passed);
    }

    [Theory]
    [InlineData(false, "bin/net11.0/Demo.dll")]
    [InlineData(true, "bin/net11.0/Demo.dll")]
    [InlineData(false, "bin/Tests in Review/net11.0/Demo.dll")]
    [InlineData(true, "bin/Tests in Review/net11.0/Demo.dll")]
    public void A_named_no_match_excludes_the_source_of_a_delayed_verbose_result(bool noticeFirst, string source)
    {
        var noMatch = $"No test matches the given testcase filter `Demo.Fixture.Match` in {source}.";
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Demo.dll (net10.0)
            Test run for {source} (.NETCoreApp,Version=v11.0)
            {(noticeFirst ? noMatch : "")}
              Passed Demo.Fixture.Match [1 ms]
            Test Run Successful.
            Total tests: 1
                 Passed: 1
            {(noticeFirst ? "" : noMatch)}
            """;

        AssertBalancedSummary(VstestOutputParser.Parse(output, 0).Summary, total: 1, passed: 1, failed: 0, skipped: 0);
    }

    [Theory]
    [InlineData(@"C:\src\Tests in Review\bin\net11.0\Demo.dll", "FullyQualifiedName~Demo.Fixture.Match")]
    [InlineData("/src/Tests ` in Review/bin/net11.0/Demo.dll", "DisplayName~A ` in B")]
    public void No_match_uses_a_complete_run_source_when_filter_and_path_contain_separators(string source, string filter)
    {
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Demo.dll (net10.0)
            Test run for {source} (.NETCoreApp,Version=v11.0)
            No test matches the given testcase filter `{filter}` in {source}
            """;
        var parsed = VstestOutputParser.Parse(output, 0);
        var report = VstestOutputParser.BuildMarkdownReport(parsed, 0, output, filter, null, requireFilterMatch: true);
        var mismatchedReport = VstestOutputParser.BuildMarkdownReport(
            parsed, 0, output, filter + ".Other", null, requireFilterMatch: true);

        AssertBalancedSummary(parsed.Summary, total: 1, passed: 1, failed: 0, skipped: 0);
        Assert.Contains("Filtered tests passed", report, StringComparison.Ordinal);
        Assert.Contains("**Status:** partial", mismatchedReport, StringComparison.Ordinal);
        Assert.Contains("Confirmed lower bounds: 1 passed", mismatchedReport, StringComparison.Ordinal);
    }

    [Fact]
    public void No_match_with_two_possible_run_sources_keeps_totals_unknown()
    {
        const string FirstSource = "/src/Tests ` in /other/bin/Demo.dll";
        const string SecondSource = "/other/bin/Demo.dll";
        const string Output = $"""
            Test run for {FirstSource} (.NETCoreApp,Version=v10.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Demo.dll (net10.0)
            Test run for {SecondSource} (.NETCoreApp,Version=v11.0)
            No test matches the given testcase filter `Demo.Fixture.Match` in {FirstSource}
            """;

        var parsed = VstestOutputParser.Parse(Output, 0);

        Assert.Null(parsed.Summary);
        Assert.Equal(1, parsed.IncompleteCounts?.Passed);
    }

    [Theory]
    [InlineData("Test Run Aborted.", 1, 0)]
    [InlineData("Test Run Aborted.\nTotal tests: Unknown", 1, 0)]
    [InlineData("Total tests: Unknown\n     Passed: 2\n     Failed: 1", 3, 1)]
    [InlineData("Test run for bin/net12.0/Demo.dll (.NETCoreApp,Version=v12.0)", 1, 0)]
    public void Incomplete_runs_do_not_count_delayed_verbose_cases_as_another_execution(string ending, int passed, int failed)
    {
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms - Demo.dll (net10.0)
            Test run for bin/net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
              Passed Demo.Fixture.Match [1 ms]
            Test Run Successful.
            Total tests: 1
                 Passed: 1
            {ending}
            """;

        var parsed = VstestOutputParser.Parse(output, 1);

        Assert.Null(parsed.Summary);
        Assert.Equal(passed, parsed.IncompleteCounts?.Passed);
        Assert.Equal(failed, parsed.IncompleteCounts?.Failed);
    }

    [Theory]
    [InlineData("net10.0", false)]
    [InlineData("net11.0", false)]
    [InlineData("net10.0", true)]
    [InlineData("net11.0", true)]
    public void A_no_match_notice_can_finish_either_pending_target(string framework, bool summaryFirst)
    {
        var noMatch = $"No test matches the given testcase filter in bin/{framework}/Demo.dll";
        const string Summary = "Total tests: 1\n    Passed: 1";
        var output = $"""
            Test run for bin/net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Test run for bin/net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            {(summaryFirst ? Summary : noMatch)}
            {(summaryFirst ? noMatch : Summary)}
            """;

        AssertBalancedSummary(VstestOutputParser.Parse(output, 0).Summary, total: 1, passed: 1, failed: 0, skipped: 0);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("No test matches the given testcase filter in Other.dll", null)]
    [InlineData("No test matches the given testcase filter in net10.0/Demo.dll", null)]
    [InlineData("No test matches the given testcase filter `Demo.Fixture.Match` in outside in net11.0/Demo.dll", null)]
    [InlineData("No test matches the given testcase filter in net11.0/Demo.dll", 1)]
    public void A_started_target_needs_a_summary_or_its_own_no_match_notice(string completion, int? expectedTotal)
    {
        var output = $"""
            Test run for net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Total tests: 1
                Passed: 1
            Test run for net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            {completion}
            """;
        var result = VstestOutputParser.Parse(output, 0);

        Assert.Equal(expectedTotal, result.Summary?.Total);
        if (expectedTotal is null)
        {
            Assert.Equal(1, result.IncompleteCounts?.Passed);
        }
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(3, 4)]
    public void Overlapping_run_starts_require_distinguishable_summaries(int numericPassed, int? expectedTotal)
    {
        var output = $"""
            Test run for net10.0/Demo.dll (.NETCoreApp,Version=v10.0)
            Test run for net11.0/Demo.dll (.NETCoreApp,Version=v11.0)
            Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
            Total tests: {numericPassed}
                Passed: {numericPassed}
            """;
        var result = VstestOutputParser.Parse(output, 0);

        Assert.Equal(expectedTotal, result.Summary?.Total);
        if (expectedTotal is null)
        {
            Assert.Equal(1, result.IncompleteCounts?.Passed);
        }
    }

    [Fact]
    public void Parse_all_total_only_blocks_leave_summary_null()
    {
        const string output = """
            Total tests: 4
             Total time: 1.0 Seconds

            Total tests: 8
             Total time: 2.0 Seconds
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.True(result.HasRecognizedSummary);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void Parse_mix_total_only_and_full_block_is_fail_closed()
    {
        const string output = """
            Total tests: 4
             Total time: 1.0 Seconds

            Test Run Successful.
            Total tests: 3
                 Passed: 3
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.True(result.HasRecognizedSummary);
        Assert.Null(result.Summary);
        Assert.NotEqual(7, result.Summary?.Total);
        Assert.NotEqual(3, result.Summary?.Passed);
    }

    [Fact]
    public void Parse_zero_total_without_counts_aggregates_with_full_block()
    {
        const string output = """
            Total tests: 0
             Total time: 0.1 Seconds

            Test Run Successful.
            Total tests: 4
                 Passed: 4
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        AssertBalancedSummary(result.Summary, total: 4, passed: 4, failed: 0, skipped: 0);
    }

    [Fact]
    public void Parse_does_not_take_skipped_from_later_assembly_block()
    {
        // Fork symptom: first Total:1 + later Skipped:2 leaked across assemblies.
        const string output = """
            Test Run Successful.
            Total tests: 1
                 Passed: 1

            Test Run Successful.
            Total tests: 253
                 Passed: 251
                Skipped: 2
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 0);
        Assert.False(result.IsPartialSuccess);
        AssertBalancedSummary(result.Summary, total: 254, passed: 252, failed: 0, skipped: 2);
        Assert.NotEqual(1, result.Summary?.Total);
    }

    [Fact]
    public void Parse_counts_beyond_24_lines_still_bound_to_own_total_tests_block()
    {
        var noise = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"  log line {i}"));
        var output = $"""
            Total tests: 2
            {noise}
                 Passed: 2

            Total tests: 1
                 Failed: 1
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(result.IsPartialSuccess);
        AssertBalancedSummary(result.Summary, total: 3, passed: 2, failed: 1, skipped: 0);
    }

    [Fact]
    public void TryParseCountsNearTotalTestsLine_stops_at_next_total_tests()
    {
        var lines = """
            Total tests: 1
                 Passed: 1
            Total tests: 2
                Skipped: 2
            """.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var firstIdx = Array.FindIndex(lines, static l => l.Trim().StartsWith("Total tests: 1", StringComparison.OrdinalIgnoreCase));
        var secondIdx = Array.FindIndex(lines, static l => l.Trim().StartsWith("Total tests: 2", StringComparison.OrdinalIgnoreCase));
        Assert.True(firstIdx >= 0 && secondIdx > firstIdx);

        var first = VstestOutputParser.TryParseCountsNearTotalTestsLine(lines, firstIdx, out _);
        AssertBalancedSummary(first, total: 1, passed: 1, failed: 0, skipped: 0);
        Assert.NotEqual(2, first?.Skipped);

        var second = VstestOutputParser.TryParseCountsNearTotalTestsLine(lines, secondIdx, out _);
        AssertBalancedSummary(second, total: 2, passed: 0, failed: 0, skipped: 2);
    }

    private static void AssertBalancedSummary(
        VstestOutputParser.TestSummary? summary,
        int total,
        int passed,
        int failed,
        int skipped)
    {
        Assert.NotNull(summary);
        Assert.Equal(total, summary.Total);
        Assert.Equal(passed, summary.Passed);
        Assert.Equal(failed, summary.Failed);
        Assert.Equal(skipped, summary.Skipped);
        Assert.Equal(summary.Total, summary.Passed + summary.Failed + summary.Skipped);
    }
}
