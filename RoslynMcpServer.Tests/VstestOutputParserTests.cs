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

    [Fact]
    public void BuildMarkdownReport_xunit_multi_project_matching_test_with_nonzero_exit_passes()
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
            requireFilterMatch: true);

        Assert.Contains("## Filtered tests passed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Tests Failed", md, StringComparison.Ordinal);
        Assert.DoesNotContain("no matching tests", md, StringComparison.Ordinal);
        Assert.Contains("non-zero", md, StringComparison.Ordinal);
        Assert.Contains("exit", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_Exit code is non-zero", md, StringComparison.Ordinal);
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
    [InlineData(0)]
    [InlineData(1)]
    public void BuildMarkdownReport_all_assemblies_no_match_reports_filtered_no_match(int exitCode)
    {
        const string output = """
            No test matches the given testcase filter `FullyQualifiedName~ZZZ_NO_SUCH_TEST_ZZZ` in C:\src\bin\Debug\net10.0\A.dll
            No test matches the given testcase filter `FullyQualifiedName~ZZZ_NO_SUCH_TEST_ZZZ` in C:\src\bin\Debug\net10.0\B.dll
            """;
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
    public void Parse_end_summary_wins_over_fail_only_totals_block()
    {
        // Source order: end-summary → totals blocks. Do not merge formats and do not
        // let a later fail-only block replace or add to Passed! counts.
        const string output = """
            Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
            Test Run Failed.
            Total tests: 2
                 Failed: 2
            """;
        var result = VstestOutputParser.Parse(output, exitCode: 1);
        Assert.False(result.IsPartialSuccess);
        Assert.True(result.HasRecognizedSummary);
        AssertBalancedSummary(result.Summary, total: 5, passed: 5, failed: 0, skipped: 0);
        Assert.NotEqual(7, result.Summary?.Total);
        Assert.NotEqual(2, result.Summary?.Failed);
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

        var first = VstestOutputParser.TryParseCountsNearTotalTestsLine(lines, firstIdx);
        AssertBalancedSummary(first, total: 1, passed: 1, failed: 0, skipped: 0);
        Assert.NotEqual(2, first?.Skipped);

        var second = VstestOutputParser.TryParseCountsNearTotalTestsLine(lines, secondIdx);
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
