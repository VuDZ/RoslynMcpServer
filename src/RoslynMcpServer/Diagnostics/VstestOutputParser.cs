using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Parses <c>dotnet test</c> console output (xUnit / VSTest / NUnit) and ignores MSBuild noise.</summary>
public static class VstestOutputParser
{
    private const int MaxFailedTestDetails = VstestTrxReader.MaxFailedTestDetails;
    private const int PartialSuccessTailChars = 2048;
    private const string NoMatchPrefix = "No test matches the given testcase filter";

    /// <summary>VSTest console duration: <c>[&lt; 1 ms]</c>, <c>[12 ms]</c>, <c>[1 s]</c>, <c>[1 m 28 s]</c>, <c>[1 h 2 m]</c>.</summary>
    private const string VstestDurationBracket =
        @"\[(?:< 1 ms|\d+(?:\.\d+)?\s*(?:ms|s|m|h)(?:\s+\d+(?:\.\d+)?\s*(?:ms|s|m|h))*)\]";

    private static readonly Regex RxXunitFailLine = new(
        @"^\[xUnit\.net[^\]]*\]\s+(?<name>.+?)\s+\[FAIL\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Console logger: display name may be FQN, <c>Class.Method</c>, method-only (<c>methodDisplay=method</c>),
    /// or Theory <c>Name(args)</c>. Duration is the line terminator so MSBuild <c>Passed:</c> counters do not match.
    /// </summary>
    private static readonly Regex RxVstestPassedLine = new(
        $@"^\s*Passed\s+(?<name>.+?)(?:\s*\(.*\))?\s+{VstestDurationBracket}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxVstestFailedLineWithDuration = new(
        $@"^\s*Failed\s+(?<name>.+?)\s+{VstestDurationBracket}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxVstestFailedLineDotted = new(
        @"^\s*Failed\s+(?<name>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]+)+(?:\s*\(.*\))?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxNunitFailedLine = new(
        @"^\s*Failed\s*:\s*(?<name>[A-Za-z_][\w]+(?:\.[A-Za-z_][\w]+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxEndSummaryLine = new(
        @"(?<kind>Passed|Failed)!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+)(?:,\s*Skipped:\s*(?<skipped>\d+))?(?:,\s*Total:\s*(?<total>\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxTotalTests = new(
        @"^[ \t]*Total tests:[ \t]*(?:(?<total>\d+)|Unknown)[ \t]*\r?$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxPassedCountLine = new(
        @"^\s*Passed:\s*(?<n>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxFailedCountLine = new(
        @"^\s*Failed:\s*(?<n>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxSkippedCountLine = new(
        @"^\s*Skipped:\s*(?<n>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxTestRunSuccessful = new(
        @"Test Run Successful\.?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxTestRunFailure = new(
        @"^[ \t]*(?:Test Run (?:Aborted|Failed)\.?|The active test run was aborted\.(?:[ \t]+Reason:[^\r\n]*)?)[ \t]*\r?$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>VSTest console block: Total tests + Passed (Failed/Skipped optional). Fail-only .slnx blocks are parsed line-wise.</summary>
    private static readonly Regex RxVstestTotalsBlock = new(
        @"Total tests:\s*(?<total>\d+)(?:[\s\S]{0,2000}?)\s+Passed:\s*(?<passed>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record TestSummary(int Total, int Passed, int Failed, int Skipped);

    public sealed record FailedTestDetail(string Name, string Error, string Stack, string StdOut = "", string StdErr = "");

    internal static bool IsSilentUnparsedFailure(string combinedOutput)
    {
        if (HasTestExecutionMarkers(combinedOutput))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return true;
        }

        return combinedOutput.Contains("Restore target(s)", StringComparison.OrdinalIgnoreCase)
               || combinedOutput.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildTrxMarkdownReport(
        VstestTrxResult result, int exitCode, string combinedOutput, string? filter, string? filterDescription,
        TestOutputReportOptions outputOptions)
    {
        var sb = new StringBuilder();
        if (result.NoFilterMatches)
        {
            AppendFilteredNoMatch(sb, filter, filterDescription, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            sb.AppendLine("## Filtered test run");
            sb.AppendLine();
            sb.AppendLine($"**Filter:** `{EscapeMdBackticks(filter)}`");
            if (!string.IsNullOrWhiteSpace(filterDescription))
            {
                sb.AppendLine($"**Match:** {filterDescription}");
            }
            sb.AppendLine();
        }

        var passed = result.Outcome == TestProcessOutcome.Passed;
        sb.AppendLine(result.Outcome switch
        {
            TestProcessOutcome.Passed => filter is null ? "## All tests passed successfully!" : "## Filtered tests passed",
            TestProcessOutcome.Failed when result.Summary is { Failed: > 0 } summary => $"❌ {summary.Failed} Tests Failed.",
            TestProcessOutcome.Failed => "## Test run failed",
            _ => "**Status:** partial",
        });
        sb.AppendLine();
        if (!passed)
        {
            sb.AppendLine($"The process exited with code `{exitCode}`.");
        }
        if (result.Summary is { } totals)
        {
            var label = passed ? "Total" : "TRX reported total";
            sb.AppendLine($"{label}: **{totals.Total}** · Passed: **{totals.Passed}** · Failed: **{totals.Failed}**"
                + (totals.Skipped > 0 ? $" · Skipped: **{totals.Skipped}**" : string.Empty));
        }
        if (passed && filter is not null && result.PassedTestNames.Count > 0)
        {
            sb.AppendLine();
            var preview = result.PassedTestNames.Select(name => $"`{EscapeMdBackticks(name)}`");
            sb.AppendLine("Matched test methods (preview): " + string.Join(", ", preview));
        }
        if (!passed)
        {
            sb.AppendLine();
            if (result.Issue is { } issue)
            {
                sb.AppendLine(TruncatedProcessLog.BuildTruncatedExcerpt(issue));
            }
            AppendFailureDetails(sb, result.Failures, result.FailureDetailsTruncated, result.BuildError, outputOptions);
            if (result.Failures.Count == 0)
            {
                AppendRawTail(sb, combinedOutput);
            }
        }
        return sb.ToString().TrimEnd();
    }

    internal static bool IsSummaryCaptureLine(ReadOnlySpan<char> line) =>
        RxEndSummaryLine.IsMatch(line)
        || IsTestRunStart(line)
        || RxTotalTests.IsMatch(line)
        || RxPassedCountLine.IsMatch(line)
        || RxFailedCountLine.IsMatch(line)
        || RxSkippedCountLine.IsMatch(line)
        || RxTestRunFailure.IsMatch(line)
        || RxTestRunSuccessful.IsMatch(line)
        || _noMatchNoticeLine.IsMatch(line);

    private static void AppendFilteredNoMatch(
        StringBuilder sb,
        string? filter,
        string? filterDescription,
        string combinedOutput)
    {
        sb.AppendLine("## Filtered test run — no matching tests");
        sb.AppendLine();
        // Rapid TIA reports its selection filter in the project headline and passes no filter here.
        sb.AppendLine(filter is null
            ? "VSTest confirmed zero matches for the selected filter in every expected run."
            : $"VSTest confirmed zero matches for `{EscapeMdBackticks(filter)}` in every expected run.");
        sb.AppendLine();
        sb.AppendLine(
            "**Agent signal:** zero tests matched the filter (build may still show `0 Error(s)`). "
            + "Do not assume the test is missing from the repo — verify Roslyn workspace scope with `get_test_list` after `load_workspace` on the test `.sln`/`.slnx`.");
        if (!string.IsNullOrWhiteSpace(filterDescription))
        {
            sb.AppendLine($"**Match mode:** {filterDescription}");
        }

        sb.AppendLine();
        AppendRawTail(sb, combinedOutput);
    }

    private static void AppendRawTail(StringBuilder sb, string combinedOutput)
    {
        var stripped = TruncatedProcessLog.StripTrailingMsBuildOutcome(combinedOutput);
        var window = TryGetAssertionWindow(stripped) ?? stripped;

        sb.AppendLine();
        sb.AppendLine($"Raw output (last {PartialSuccessTailChars} chars):");
        sb.AppendLine();
        sb.AppendLine("```text");
        if (string.IsNullOrEmpty(window))
        {
            sb.AppendLine("(empty)");
        }
        else
        {
            sb.AppendLine(TruncatedProcessLog.BuildTruncatedExcerpt(window, PartialSuccessTailChars).TrimEnd());
        }

        sb.AppendLine("```");
    }

    /// <summary>
    /// Prefer the VSTest assertion + stack over a log tail that is only Standard Output plus the MSBuild footer.
    /// </summary>
    private static string? TryGetAssertionWindow(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var emIdx = text.IndexOf("Error Message:", StringComparison.OrdinalIgnoreCase);
        if (emIdx < 0)
        {
            return null;
        }

        var stdoutIdx = IndexOfEarliest(text, emIdx + "Error Message:".Length, "Standard Output Messages:", "Standard Output:");
        var end = stdoutIdx >= 0 ? stdoutIdx : text.Length;
        var window = text[emIdx..end].Trim();
        return string.IsNullOrEmpty(window) ? null : window;
    }

    private static bool HasTestExecutionMarkers(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (HasSummaryMarkers(text)
            || text.Contains("Test Run Failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd();
            if (RxVstestPassedLine.IsMatch(trimmed) || TryGetFailedTestName(trimmed, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSummaryMarkers(string text) =>
        RxEndSummaryLine.IsMatch(text)
        || RxTotalTests.IsMatch(text)
        || RxTestRunSuccessful.IsMatch(text)
        || RxVstestTotalsBlock.IsMatch(text);

    private static bool IsTestRunStart(ReadOnlySpan<char> line) =>
        line.TrimStart().StartsWith("Test run for ", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetFailedTestName(string trimmed, out string name)
    {
        var matched = TryGetFailedTestDisplayName(trimmed, out var displayName, out _);
        name = StripTrailingTheoryArguments(displayName);
        return matched;
    }

    private static bool TryGetFailedTestDisplayName(string trimmed, out string displayName, out bool isAdapterNotice)
    {
        displayName = string.Empty;

        var adapterNotice = RxXunitFailLine.Match(trimmed);
        isAdapterNotice = adapterNotice.Success;
        if (isAdapterNotice)
        {
            displayName = adapterNotice.Groups["name"].Value.Trim();
            return IsPlausibleTestName(StripTrailingTheoryArguments(displayName));
        }

        var nunit = RxNunitFailedLine.Match(trimmed);
        if (nunit.Success)
        {
            displayName = nunit.Groups["name"].Value.Trim();
            return IsPlausibleTestName(displayName);
        }

        var withDuration = RxVstestFailedLineWithDuration.Match(trimmed);
        if (withDuration.Success)
        {
            if (IsRestoreOrPruneFailureLine(trimmed))
            {
                return false;
            }

            displayName = withDuration.Groups["name"].Value.Trim();
            var name = StripTrailingTheoryArguments(displayName);
            return !string.IsNullOrWhiteSpace(name)
                   && !name.StartsWith("to ", StringComparison.OrdinalIgnoreCase);
        }

        var dotted = RxVstestFailedLineDotted.Match(trimmed);
        if (dotted.Success)
        {
            displayName = dotted.Groups["name"].Value.Trim();
            return IsPlausibleTestName(StripTrailingTheoryArguments(displayName));
        }

        return false;
    }

    private static string StripTrailingTheoryArguments(string name)
    {
        var open = name.IndexOf('(');
        return open > 0 ? name[..open].TrimEnd() : name;
    }

    /// <summary>
    /// Restore/prune console lines, not a test whose display name happens to contain <c>MSBuild</c>.
    /// </summary>
    private static bool IsRestoreOrPruneFailureLine(string line) =>
        line.Contains("prune package", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Failed to load", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Done executing", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlausibleTestName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.StartsWith("to ", StringComparison.OrdinalIgnoreCase)
        && name.Contains('.', StringComparison.Ordinal);

    private static int IndexOfEarliest(string text, int start, params string[] tokens)
    {
        var best = -1;
        foreach (var token in tokens)
        {
            var i = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best < 0 || i < best))
            {
                best = i;
            }
        }

        return best;
    }

    private static void AppendFailureDetails(
        StringBuilder sb,
        IReadOnlyList<FailedTestDetail> failures,
        bool failuresTruncated,
        string? buildError,
        TestOutputReportOptions outputOptions)
    {
        if (buildError is not null)
        {
            sb.AppendLine($"**MSBuild error:** {EscapeMdBackticks(TruncatedProcessLog.BuildTruncatedExcerpt(buildError))}");
        }

        if (failures.Count == 0)
        {
            return;
        }

        var stdoutBudget = outputOptions.StdOutBudget;
        var stderrBudget = outputOptions.StdErrBudget;
        var (stdoutHead, stdoutTail) = TestOutputReportOptions.ScaleHeadTail(
            stdoutBudget,
            TestOutputReportOptions.DefaultStdOutChars,
            TestOutputReportOptions.DefaultStdOutHeadChars,
            TestOutputReportOptions.DefaultStdOutTailChars);
        var (stderrHead, stderrTail) = TestOutputReportOptions.ScaleHeadTail(
            stderrBudget,
            TestOutputReportOptions.DefaultStdErrChars,
            TestOutputReportOptions.DefaultStdErrHeadChars,
            TestOutputReportOptions.DefaultStdErrTailChars);

        for (var i = 0; i < failures.Count; i++)
        {
            var f = failures[i];
            var caseSuffix = FormatFailureCaseSuffix(failures, i);
            sb.AppendLine($"{i + 1}. **TestName:** `{EscapeMdBackticks(f.Name)}`{caseSuffix}");
            if (!string.IsNullOrEmpty(f.Error))
            {
                if (f.Error.Contains('\n', StringComparison.Ordinal))
                {
                    sb.AppendLine("   **Error:**");
                    sb.AppendLine();
                    sb.AppendLine("```text");
                    sb.AppendLine(f.Error.Replace("```", "'''", StringComparison.Ordinal).TrimEnd());
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine($"   **Error:** {EscapeMdBackticks(f.Error)}");
                }
            }

            if (!string.IsNullOrEmpty(f.Stack))
            {
                var stackOneLine = f.Stack.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
                sb.AppendLine($"   **Stack:** {EscapeMdBackticks(stackOneLine)}");
            }

            AppendFailureStream(sb, "StdOut", f.StdOut, stdoutBudget, stdoutHead, stdoutTail, outputOptions);
            AppendFailureStream(sb, "StdErr", f.StdErr, stderrBudget, stderrHead, stderrTail, outputOptions);

            sb.AppendLine();
        }

        if (failuresTruncated)
        {
            sb.AppendLine($"[!] Showing first {MaxFailedTestDetails} failures only.");
        }
    }

    private static string FormatFailureCaseSuffix(IReadOnlyList<FailedTestDetail> failures, int index)
    {
        // Theory display names can contain arbitrary arguments. Number equal headings without rendering those values.
        var name = failures[index].Name;
        bool HasSameName(FailedTestDetail failure) => string.Equals(failure.Name, name, StringComparison.Ordinal);
        if (failures.Count(HasSameName) == 1)
        {
            return string.Empty;
        }

        var caseNumber = failures.Take(index).Count(HasSameName) + 1;
        return $" (case {caseNumber})";
    }

    private static void AppendFailureStream(
        StringBuilder sb,
        string label,
        string raw,
        int budget,
        int head,
        int tail,
        TestOutputReportOptions outputOptions)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        var shown = TruncatedProcessLog.TruncateHeadTail(raw, budget, head, tail);
        sb.AppendLine($"   **{label}:**");
        sb.AppendLine();
        sb.AppendLine("```text");
        sb.AppendLine(shown.Replace("```", "'''", StringComparison.Ordinal).TrimEnd());
        sb.AppendLine("```");

        if (shown.Length < raw.Length)
        {
            if (budget >= TestOutputReportOptions.FullOutputSafetyCapChars)
            {
                sb.AppendLine($"   _{label} truncated at the {TestOutputReportOptions.FullOutputSafetyCapChars}-char safety cap._");
            }
            else
            {
                var hint = outputOptions.SuppressOutputOptionHints
                    ? string.Empty
                    : " Pass `includeFullOutput=true` or raise `maxOutputChars`.";
                sb.AppendLine(
                    $"   _{label} truncated to {budget} chars (head+tail).{hint}_");
            }
        }
        else if (outputOptions.IncludeFullOutput && raw.Contains(TruncatedProcessLog.MiddleMarker, StringComparison.Ordinal))
        {
            sb.AppendLine($"   _{label} was capped at parse time ({TestOutputReportOptions.ParseStreamCapChars} chars)._");
        }
    }

    private static string EscapeMdBackticks(string s) => s.Replace('`', '\'');

    // Restrict notices to a complete logger line so output quoting the message cannot prove a no-match.
    private static readonly Regex _noMatchNoticeLine = new(
        $@"^[ \t]*{NoMatchPrefix}(?:(?: `[^\r\n]*`)? in [^\r\n \t][^\r\n]*)?[ \t]*\r?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
}
