using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Parses <c>dotnet test</c> console output (xUnit / VSTest / NUnit) and ignores MSBuild noise.</summary>
public static class VstestOutputParser
{
    private const int MaxFailedTestDetails = 5;
    private const int MaxStackTraceLinesPerFailure = 15;
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

    private static readonly string[] ErrorBodyTerminators =
    [
        "Stack Trace:",
        "Standard Output Messages:",
        "Standard Output:",
        "Standard Error Messages:",
        "Standard Error:",
        "Debug Trace:",
        "Attachments:",
    ];

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

    /// <summary>
    /// Per-assembly VSTest discovery noise when a filter matches nothing in that assembly.
    /// Sibling assemblies on a <c>.sln</c> run print this even when another assembly executed the test.
    /// </summary>
    private static readonly Regex RxNoTestMatchesLine = new(
        NoMatchPrefix,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record TestSummary(int Total, int Passed, int Failed, int Skipped);

    public sealed record FailedTestDetail(string Name, string Error, string Stack, string StdOut = "", string StdErr = "");

    public sealed record ParseResult(
        TestSummary? Summary,
        bool HasRecognizedSummary,
        bool IsPartialSuccess,
        IReadOnlyList<FailedTestDetail> Failures,
        IReadOnlyList<string> PassedTestNames)
    {
        internal bool FailureDetailsTruncated { get; init; }
        internal ReportedTestCounts? IncompleteCounts { get; init; }
        internal bool SummaryTruncated { get; init; }
        internal bool HasConfirmedFailure { get; init; }
        internal string? BuildError { get; init; }
        internal bool OutputTruncated { get; init; }
    }

    public static string DeduplicateNuGetAuditLines(string combinedOutput)
    {
        if (string.IsNullOrEmpty(combinedOutput))
        {
            return combinedOutput;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var raw in combinedOutput.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var line = raw;
            if (IsNuGetAuditLine(line))
            {
                var key = line.Trim();
                if (!seen.Add(key))
                {
                    continue;
                }
            }

            sb.AppendLine(raw);
        }

        return sb.ToString().TrimEnd();
    }

    public static ParseResult Parse(string combinedOutput, int exitCode) =>
        Parse(combinedOutput, exitCode, summaryTruncated: false, outputTruncated: false);

    internal static ParseResult Parse(
        string combinedOutput,
        int exitCode,
        bool summaryTruncated,
        bool outputTruncated)
    {
        combinedOutput = DeduplicateNuGetAuditLines(combinedOutput);
        ReportedTestCounts? incompleteCounts = null;
        var reportedFailure = false;
        var summary = summaryTruncated ? null : TryParseTestSummary(combinedOutput, outputTruncated, out incompleteCounts, out reportedFailure);
        if (summaryTruncated)
        {
            incompleteCounts = ReadRetainedLowerBounds(combinedOutput);
        }
        var hasSummary = !summaryTruncated && (summary is not null || HasSummaryMarkers(combinedOutput));
        var passedNames = CollectPassedTestNames(combinedOutput);
        var failures = ParseFailedTestBlocks(combinedOutput, out var failureDetailsTruncated);
        if (incompleteCounts is { } counts && failures.Count > 0)
        {
            incompleteCounts = counts.AtLeast(new ReportedTestCounts(null, 1, null));
        }

        var isPartial = exitCode == 0 && !hasSummary;
        // Test logger messages may quote a build diagnostic in a display name or filter.
        var buildError = exitCode == 0 ? null : combinedOutput.Split('\n')
            .FirstOrDefault(line => BuildOutputReport.IsErrorDiagnostic(line) && !_testLoggerLine.IsMatch(line))?.Trim();

        return new ParseResult(summary, hasSummary, isPartial, failures, passedNames)
        {
            FailureDetailsTruncated = failureDetailsTruncated,
            IncompleteCounts = incompleteCounts,
            SummaryTruncated = summaryTruncated,
            HasConfirmedFailure = reportedFailure || incompleteCounts is { Failed: > 0 }
                || failures.Count > 0 || RxTestRunFailure.IsMatch(combinedOutput) || buildError is not null,
            BuildError = buildError,
            OutputTruncated = outputTruncated,
        };
    }

    public static bool FilterMatchedAnyTest(string? filter, string combinedOutput, IReadOnlyList<string> passedNames)
    {
        var needle = ExtractFilterNeedle(filter);
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        foreach (var name in passedNames)
        {
            if (TestNameMatchesFilterNeedle(name, needle))
            {
                return true;
            }
        }

        foreach (var name in CollectFailedTestNames(combinedOutput))
        {
            if (TestNameMatchesFilterNeedle(name, needle))
            {
                return true;
            }
        }

        foreach (var line in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd();
            if (!LooksLikeTestOutcomeLine(trimmed))
            {
                continue;
            }

            if (TryGetPassedTestName(trimmed, out var passed)
                && TestNameMatchesFilterNeedle(passed, needle))
            {
                return true;
            }

            if (TryGetFailedTestName(trimmed, out var failed)
                && TestNameMatchesFilterNeedle(failed, needle))
            {
                return true;
            }

            if (LineContainsFilterNeedle(trimmed, needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Exit ≠ 0 with no VSTest summary/failures and no test-run markers — hung restore, locked <c>obj</c>, or a
    /// compile that died without a parsed diagnostic. A VSTest <c>Build FAILED</c> / <c>0 Error(s)</c> footer after
    /// tests actually ran is not silent.
    /// </summary>
    public static bool IsSilentUnparsedFailure(ParseResult parse, string combinedOutput)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (parse.Summary is not null || parse.HasRecognizedSummary || parse.Failures.Count > 0 || parse.BuildError is not null)
        {
            return false;
        }

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

    public static string BuildMarkdownReport(
        ParseResult parse,
        int exitCode,
        string combinedOutput,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch,
        TestOutputReportOptions outputOptions = default) =>
        BuildMarkdownReport(
            parse, exitCode, combinedOutput, filter, filterDescription, requireFilterMatch, outputOptions, out _);

    internal static string BuildMarkdownReport(
        ParseResult parse,
        int exitCode,
        string combinedOutput,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch,
        TestOutputReportOptions outputOptions,
        out bool succeeded)
    {
        succeeded = false;
        var sb = new StringBuilder();

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

        // A retained failure proves the run failed even when capture dropped the matching name.
        // With passing totals, a nonzero exit alone can come from a sibling project with no matches.
        // See docs/project-review-2026-11-25/task-25-p2-filtered-test-failure-status-med.md.
        var confirmedFailure = parse.HasConfirmedFailure || (exitCode != 0 && parse.Summary is null);
        if (parse.SummaryTruncated)
        {
            sb.AppendLine(confirmedFailure ? "## Test run failed" : "**Status:** partial");
            sb.AppendLine();
            sb.AppendLine($"The process exited with code `{exitCode}`. The test summary exceeded the capture limit.");
            if (parse.IncompleteCounts is { } retainedCounts)
            {
                sb.AppendLine(retainedCounts.FormatLowerBounds());
                if (retainedCounts.Failed > 0 && parse.Failures.Count == 0)
                {
                    sb.AppendLine("The captured output does not identify which test failed.");
                }
            }

            AppendFailureDetails(sb, parse, outputOptions);
            AppendRawTail(sb, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        // One target can finish successfully before another test host crashes. Its totals cannot
        // establish the outcome of the whole command. See docs/project-review-2026-11-25/task-33-p1-test-report-false-success-hi.md.
        var failureWithoutTotals = parse.HasConfirmedFailure && parse.Summary is not { Failed: > 0 };
        var unknownFilterMatch = parse.OutputTruncated && requireFilterMatch && parse.Summary is not { Total: 0 }
            && !FilterMatchedAnyTest(filter, combinedOutput, parse.PassedTestNames);
        if (parse.IncompleteCounts is not null || failureWithoutTotals || unknownFilterMatch)
        {
            sb.AppendLine(confirmedFailure ? "## Test run failed" : "**Status:** partial");
            sb.AppendLine();
            sb.AppendLine($"The process exited with code `{exitCode}`.");
            if (unknownFilterMatch)
            {
                sb.AppendLine("Retained test names do not verify the filter match.");
            }

            if ((parse.Summary is { Failed: > 0 } || parse.IncompleteCounts is { Failed: > 0 }) && parse.Failures.Count == 0)
            {
                sb.AppendLine("The captured output does not identify which test failed.");
            }

            if (parse.IncompleteCounts is { } counts)
            {
                sb.AppendLine(counts.FormatLowerBounds());
            }
            else if (parse.Summary is { } retained)
            {
                sb.AppendLine($"Reported totals: {retained.Total} total, {retained.Passed} passed, "
                    + $"{retained.Failed} failed, {retained.Skipped} skipped.");
            }

            if (failureWithoutTotals && parse.BuildError is null && parse.IncompleteCounts is null)
            {
                sb.AppendLine("The available totals do not cover the complete run.");
            }

            AppendFailureDetails(sb, parse, outputOptions);
            AppendRawTail(sb, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        // An explicit no-match line can come from a sibling project. For truncated output
        // the captured totals must also prove zero tests ran before reporting a no-match.
        if (!parse.HasConfirmedFailure && (!parse.OutputTruncated || parse.Summary is { Total: 0 })
            && ShouldReportFilteredNoMatch(requireFilterMatch, parse.Summary, filter, combinedOutput, parse.PassedTestNames))
        {
            AppendFilteredNoMatch(sb, filter, filterDescription, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        if (parse.IsPartialSuccess)
        {
            sb.AppendLine("**Status:** partial");
            sb.AppendLine();
            sb.AppendLine("Tests completed (exit 0); no VSTest/xUnit summary line was detected.");
            sb.AppendLine();
            AppendRawTail(sb, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        if (parse.Summary is null && !parse.HasRecognizedSummary)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(filter) ? "## Test run" : "## Filtered test run");
            sb.AppendLine();
            sb.AppendLine($"No standard VSTest/xUnit summary line was detected (exit code `{exitCode}`).");
            if (parse.Failures.Count > 0)
            {
                sb.AppendLine();
                AppendFailureDetails(sb, parse, outputOptions);
            }

            if (exitCode != 0)
            {
                TruncatedProcessLog.AppendLastCharacters(
                    sb,
                    TruncatedProcessLog.BuildPreambleTestFailed(exitCode),
                    combinedOutput);
            }

            return sb.ToString().TrimEnd();
        }

        var summary = parse.Summary ?? TryParseTestSummary(combinedOutput, parse.OutputTruncated, out _, out _);
        if (summary is null)
        {
            sb.AppendLine("**Status:** partial");
            sb.AppendLine();
            sb.AppendLine("Tests completed; summary counts could not be parsed.");
            AppendRawTail(sb, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        var (total, passed, failed, skipped) = summary;

        // A no-match notice describes one assembly. It can coexist with a failure elsewhere
        // even when all reported tests passed. See docs/project-review-2026-11-25/task-45-p1-test-command-failure-cause-hi.md.
        if (exitCode == 0 && failed == 0 && total > 0)
        {
            succeeded = true;
            sb.AppendLine(string.IsNullOrWhiteSpace(filter) ? "## All tests passed successfully!" : "## Filtered tests passed");
            sb.AppendLine();
            sb.AppendLine(
                $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
                (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
            if (parse.PassedTestNames.Count > 0 && !string.IsNullOrWhiteSpace(filter))
            {
                // Theory rows count as separate test cases but repeat the same method name in the preview.
                var preview = parse.PassedTestNames.Distinct(StringComparer.Ordinal).Take(5)
                    .Select(name => $"`{EscapeMdBackticks(name)}`");
                sb.AppendLine();
                sb.AppendLine("Matched test methods (preview): " + string.Join(", ", preview));
            }

            return sb.ToString().TrimEnd();
        }

        sb.AppendLine(failed == 0 ? "## Test run failed" : $"❌ {failed} Tests Failed.");
        sb.AppendLine();
        sb.AppendLine(
            $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
            (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
        sb.AppendLine();
        AppendFailureDetails(sb, parse, outputOptions);
        if (parse.Failures.Count == 0)
        {
            sb.AppendLine(failed == 0 && exitCode != 0
                ? $"The command exited with code `{exitCode}`. The captured output does not establish the cause."
                : "_Failure details could not be parsed from the log (format may differ). Inspect raw output below._");
            AppendRawTail(sb, combinedOutput);
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
        || RxNoTestMatchesLine.IsMatch(line);

    /// <summary>
    /// Filtered no-match only when no tests executed (<c>summary</c> missing or <c>Total == 0</c>)
    /// and the log has the VSTest line <c>No test matches the given testcase filter</c>.
    /// <c>Total &gt; 0</c> (sibling no-match noise / MSTest method-only display) must not abort;
    /// 1.3.23 name-match still flags a genuine needle miss when that explicit line is absent.
    /// </summary>
    private static bool ShouldReportFilteredNoMatch(
        bool requireFilterMatch,
        TestSummary? summary,
        string? filter,
        string combinedOutput,
        IReadOnlyList<string> passedTestNames)
    {
        if (!requireFilterMatch)
        {
            return false;
        }

        var hasExplicitNoMatchLine = ContainsNoTestMatchesFilterLine(combinedOutput);
        if (summary is { Total: > 0 })
        {
            if (hasExplicitNoMatchLine)
            {
                return false;
            }

            return !FilterMatchedAnyTest(filter, combinedOutput, passedTestNames);
        }

        return hasExplicitNoMatchLine;
    }

    private static bool ContainsNoTestMatchesFilterLine(string combinedOutput)
        => !string.IsNullOrEmpty(combinedOutput) && RxNoTestMatchesLine.IsMatch(combinedOutput);

    private static void AppendFilteredNoMatch(
        StringBuilder sb,
        string? filter,
        string? filterDescription,
        string combinedOutput)
    {
        sb.AppendLine("## Filtered test run — no matching tests");
        sb.AppendLine();
        sb.AppendLine(
            $"No passed/failed test line matched the filter needle `{EscapeMdBackticks(ExtractFilterNeedle(filter) ?? filter!)}`.");
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

    private static bool IsNuGetAuditLine(string line) =>
        line.Contains("NU190", StringComparison.OrdinalIgnoreCase)
        || line.Contains("NU160", StringComparison.OrdinalIgnoreCase)
        || (line.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
            && line.Contains("warning", StringComparison.OrdinalIgnoreCase));

    private static ReportedTestCounts ReadRetainedLowerBounds(string text)
    {
        var compact = new ReportedTestCounts();
        var standalone = new ReportedTestCounts();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = RxEndSummaryLine.Match(line);
            if (match.Success)
            {
                compact = compact.Add(ReadCompactCounts(match));
            }
            else
            {
                // A lost header makes standalone counters impossible to assign to distinct runs.
                // The largest retained counter is still a lower bound even if both logger formats overlap.
                standalone = standalone.AtLeast(ReadOutcomeLine(line));
            }
        }

        return compact.AtLeast(standalone);
    }

    private static TestSummary? TryParseTestSummary(
        string text,
        bool outputTruncated,
        out ReportedTestCounts? incompleteCounts,
        out bool reportedFailure)
    {
        var result = ReadSummaryBlocks(text, outputTruncated);
        var counts = result.Counts;
        reportedFailure = counts.Failed > 0;
        incompleteCounts = result.BlockCount > 0 && result.Total is null ? counts : null;
        return result.Summary;
    }

    private static SummaryBlocks ReadSummaryBlocks(string text, bool outputTruncated)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var knownSources = lines.Where(line => IsTestRunStart(line))
            .Select(line => RunSource.Parse(line.Trim()["Test run for ".Length..]).Path)
            .ToHashSet(StringComparer.Ordinal);
        var compact = new List<LoggerSummary>();
        var numeric = new List<LoggerSummary>();
        var starts = new List<RunSource>();
        var pendingSources = new HashSet<string>(StringComparer.Ordinal);
        // A delayed normal logger may print every case and its summary after the next target starts.
        // A named no-match rules that target out, even when its notice follows those records.
        var sourcesWithoutMatches = lines.Select(line => GetNoMatchSource(line, knownSources))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var noMatches = new HashSet<string>(StringComparer.Ordinal);
        var aborted = new SummaryBlocks();
        var sawAbort = false;
        var isolatedRun = false;
        var cases = new ReportedTestCounts();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Equals("Test Run Aborted.", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("The active test run was aborted.", StringComparison.OrdinalIgnoreCase))
            {
                sawAbort = true;
                continue;
            }

            if (IsTestRunStart(line))
            {
                var source = RunSource.Parse(line["Test run for ".Length..]);
                isolatedRun = !outputTruncated && pendingSources.Count == 0 && !sourcesWithoutMatches.Contains(source.Path);
                cases = default;
                starts.Add(source);
                pendingSources.Add(source.Path);
                continue;
            }

            if (line.StartsWith(NoMatchPrefix, StringComparison.Ordinal))
            {
                if (GetNoMatchSource(lines[i], knownSources) is { } path && pendingSources.Remove(path))
                {
                    noMatches.Add(path);
                }

                continue;
            }

            var endSummary = RxEndSummaryLine.Match(line);
            if (endSummary.Success)
            {
                var counts = ReadCompactCounts(endSummary);
                var sum = (counts.Passed ?? 0) + (counts.Failed ?? 0) + (counts.Skipped ?? 0);
                var total = ReadCount(endSummary.Groups["total"]) ?? sum;
                var block = new SummaryBlocks(1, total == sum ? total : null, counts);
                var (firstStart, lastStart) = FindCompactStarts(line, starts, pendingSources);
                compact.Add(new LoggerSummary(block, firstStart, lastStart));
                if (compact.Count >= starts.Count)
                {
                    pendingSources.Clear();
                }

                continue;
            }

            var header = RxTotalTests.Match(line);
            if (header.Success)
            {
                var summary = TryParseCountsNearTotalTestsLine(lines, i, out var counts);
                var block = new SummaryBlocks(1, summary?.Total, counts);
                if (header.Groups["total"].Success)
                {
                    // A normal logger emits individual cases before its totals. When the previous
                    // run had finished before this one started, matching cases identify the new run.
                    // See docs/project-review-2026-11-25/task-46-p2-vstest-sequential-run-identity-hi.md.
                    var identified = isolatedRun && summary is { Total: > 0 }
                        && cases.Passed.GetValueOrDefault() == summary.Passed
                        && cases.Failed.GetValueOrDefault() == summary.Failed
                        && cases.Skipped.GetValueOrDefault() == summary.Skipped;
                    numeric.Add(new LoggerSummary(block, identified ? starts.Count : 1, starts.Count));
                    if (identified)
                    {
                        pendingSources.Remove(starts[^1].Path);
                    }

                    if (numeric.Count >= starts.Count)
                    {
                        pendingSources.Clear();
                    }
                }
                else
                {
                    // An aborted run has no completed compact summary of its own.
                    aborted = aborted.Add(block);
                }

                cases = default;
            }
            else if (isolatedRun && _individualTestOutcome.IsMatch(line))
            {
                cases = line[0] switch
                {
                    'P' => cases with { Passed = cases.Passed.GetValueOrDefault() + 1 },
                    'F' => cases with { Failed = cases.Failed.GetValueOrDefault() + 1 },
                    _ => cases with { Skipped = cases.Skipped.GetValueOrDefault() + 1 }
                };
            }
        }

        // No-match notices and zero totals can refer to the same source. Only the excess
        // of distinct, named no-match sources accounts for starts without a summary.
        var emptySummaries = Math.Max(compact.Count(item => item.Block.Total == 0), numeric.Count(item => item.Block.Total == 0));
        var noMatchWithoutTotals = Math.Max(0, noMatches.Count - emptySummaries);
        // Verbose case lines can arrive late. Their position identifies a run only if every start
        // has a complete outcome; it cannot increase a lower bound for an interrupted command.
        if (!sawAbort && aborted.BlockCount == 0 && numeric.Any(item => item.FirstStart > 1))
        {
            var candidate = MergeLoggerSummaries(compact, numeric, starts.Count, useCasePositions: true);
            if (candidate.Total.HasValue && candidate.BlockCount + noMatchWithoutTotals == starts.Count)
            {
                return candidate;
            }
        }

        var complete = MergeLoggerSummaries(compact, numeric, starts.Count, useCasePositions: false);
        var result = complete.Add(aborted);
        return result.BlockCount + noMatchWithoutTotals < starts.Count ? result with { Total = null } : result;
    }

    private static string? GetNoMatchSource(string line, HashSet<string> knownSources)
    {
        line = line.TrimStart();
        if (!line.StartsWith(NoMatchPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var fields = line[NoMatchPrefix.Length..];
        const string AbbreviatedSeparator = " in ";
        if (fields.StartsWith(AbbreviatedSeparator, StringComparison.Ordinal))
        {
            return MatchKnownSource(fields[AbbreviatedSeparator.Length..]);
        }

        const string OpeningFilter = " `";
        const string ClosingFilter = "` in ";
        if (!fields.StartsWith(OpeningFilter, StringComparison.Ordinal))
        {
            return null;
        }

        // VSTest inserts the filter and path verbatim. Either can contain the closing delimiter,
        // so trust only a unique full source path from a run header.
        // See docs/project-review-2026-11-25/task-48-p2-vstest-no-match-source-path-med.md.
        string? match = null;
        var start = OpeningFilter.Length;
        while (start < fields.Length)
        {
            var separator = fields.IndexOf(ClosingFilter, start, StringComparison.Ordinal);
            if (separator < 0)
            {
                break;
            }

            start = separator + ClosingFilter.Length;
            if (MatchKnownSource(fields[start..]) is { } source)
            {
                if (match is not null)
                {
                    return null;
                }

                match = source;
            }
        }

        return match;

        string? MatchKnownSource(string source)
        {
            if (knownSources.Contains(source))
            {
                return source;
            }

            // Some retained logs punctuate the notice. Prefer the actual path with its period if known.
            return source.EndsWith('.') && knownSources.Contains(source[..^1]) ? source[..^1] : null;
        }
    }

    private static (int First, int Last) FindCompactStarts(string line, List<RunSource> starts, HashSet<string> pendingSources)
    {
        if (!line.EndsWith(')'))
        {
            return (1, starts.Count);
        }

        var suffix = line.LastIndexOf(" - ", StringComparison.Ordinal);
        if (suffix < 0)
        {
            return (1, starts.Count);
        }

        var source = RunSource.Parse(line[(suffix + 3)..]);
        var first = starts.FindIndex(start => start.Matches(source));
        if (first < 0)
        {
            return (1, starts.Count);
        }

        var last = starts.FindLastIndex(start => start.Matches(source));
        if (first == last)
        {
            pendingSources.Remove(starts[first].Path);
        }

        return (first + 1, last + 1);
    }

    private static SummaryBlocks MergeLoggerSummaries(
        List<LoggerSummary> compact,
        List<LoggerSummary> numeric,
        int started,
        bool useCasePositions)
    {
        var compactSum = compact.Aggregate(default(SummaryBlocks), (sum, item) => sum.Add(item.Block));
        var numericSum = numeric.Aggregate(default(SummaryBlocks), (sum, item) => sum.Add(item.Block));
        if (compact.Count == 0 || numeric.Count == 0)
        {
            return compactSum.Add(numericSum);
        }

        // A logger may finish printing after the next target starts. Pair identical complete
        // summaries across the whole stream, keeping repetitions within a format as separate runs.
        // See docs/project-review-2026-11-25/task-41-p2-mixed-vstest-summary-formats-hi.md.
        if (started > 0 && compactSum.Total.HasValue && numericSum.Total.HasValue)
        {
            // Numeric records arrive in order of their last possible start. Pair the earliest-ending
            // overlapping candidate first so later, more restricted records can still match.
            var available = compact.GroupBy(item => item.Block.Summary!)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LastStart).ToList());
            var merged = compactSum;
            foreach (var item in numeric)
            {
                if (available.TryGetValue(item.Block.Summary!, out var candidates))
                {
                    var match = candidates.FindIndex(candidate => candidate.FirstStart <= item.LastStart
                        && (!useCasePositions || item.FirstStart <= candidate.LastStart));
                    if (match >= 0)
                    {
                        candidates.RemoveAt(match);
                        continue;
                    }
                }

                merged = merged.Add(item.Block);
            }

            if (merged.BlockCount <= started)
            {
                return merged;
            }
        }

        // Missing boundaries or conflicting counts leave only a lower bound for each outcome.
        return new SummaryBlocks(Math.Max(compact.Count, numeric.Count), null, compactSum.Counts.AtLeast(numericSum.Counts));
    }

    private static bool IsTestRunStart(ReadOnlySpan<char> line) =>
        line.TrimStart().StartsWith("Test run for ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Line-wise counts for one <c>Total tests:</c> block. Stops at the next <c>Total tests:</c> (no whole-log leak).</summary>
    internal static TestSummary? TryParseCountsNearTotalTestsLine(
        string[] lines,
        int totalTestsLineIndex,
        out ReportedTestCounts counts)
    {
        counts = default;
        var tm = RxTotalTests.Match(lines[totalTestsLineIndex].Trim());
        if (!tm.Success)
        {
            return null;
        }

        var total = ReadCount(tm.Groups["total"]);

        for (var j = totalTestsLineIndex; j < lines.Length; j++)
        {
            var trimmed = lines[j].Trim();
            if (j > totalTestsLineIndex && (RxTotalTests.IsMatch(trimmed) || RxEndSummaryLine.IsMatch(trimmed) || IsTestRunStart(trimmed)))
            {
                break;
            }

            counts = counts.AtLeast(ReadOutcomeLine(trimmed));
        }

        // .slnx / MSBuild VSTest target often omits Passed: when every test failed (and Failed: on all-pass).
        // Only infer Passed when Failed or Skipped was present — Total-only stays unparsed.
        if (counts.Passed is null && total is { } numeric && (counts.Failed is not null || counts.Skipped is not null))
        {
            counts = counts with { Passed = Math.Max(0, numeric - (counts.Failed ?? 0) - (counts.Skipped ?? 0)) };
        }

        var (passed, failed, skipped) = counts;
        return total is { } known && known == (passed ?? 0) + (failed ?? 0) + (skipped ?? 0)
            ? new TestSummary(known, passed ?? 0, failed ?? 0, skipped ?? 0)
            : null;
    }

    private static ReportedTestCounts ReadOutcomeLine(string line) => new(
        ReadCount(RxPassedCountLine.Match(line).Groups["n"]),
        ReadCount(RxFailedCountLine.Match(line).Groups["n"]),
        ReadCount(RxSkippedCountLine.Match(line).Groups["n"]));

    private static ReportedTestCounts ReadCompactCounts(Match match) => new(
        ReadCount(match.Groups["passed"]), ReadCount(match.Groups["failed"]), ReadCount(match.Groups["skipped"]));

    private static int? ReadCount(Group group) =>
        group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : null;

    private static IReadOnlyList<string> CollectPassedTestNames(string text)
    {
        var names = new List<string>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryGetPassedTestName(line.TrimEnd(), out var name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> CollectFailedTestNames(string text)
    {
        var names = new List<string>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd();
            if (TryGetFailedTestName(trimmed, out var name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<FailedTestDetail> ParseFailedTestBlocks(string text, out bool truncated)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var blocks = new List<(int StartLine, string DisplayName, bool IsAdapterNotice)>();
        var detailCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            if (TryGetFailedTestDisplayName(lines[i].TrimEnd(), out var displayName, out var isAdapterNotice))
            {
                blocks.Add((i, displayName, isAdapterNotice));
                if (!isAdapterNotice)
                {
                    detailCounts[displayName] = detailCounts.GetValueOrDefault(displayName) + 1;
                }
            }
        }

        var result = new List<FailedTestDetail>();
        truncated = false;
        for (var b = 0; b < blocks.Count; b++)
        {
            var (start, displayName, isAdapterNotice) = blocks[b];
            // The xUnit adapter can announce the same case before the console logger prints its details.
            if (isAdapterNotice && detailCounts.TryGetValue(displayName, out var remaining) && remaining > 0)
            {
                detailCounts[displayName] = remaining - 1;
                continue;
            }

            if (result.Count == MaxFailedTestDetails)
            {
                truncated = true;
                break;
            }

            var end = b + 1 < blocks.Count ? blocks[b + 1].StartLine : lines.Length;
            var blockText = string.Join(Environment.NewLine, lines[start..end]);
            ExtractFailureFields(blockText, out var error, out var stack, out var stdOut, out var stdErr);
            result.Add(new FailedTestDetail(StripTrailingTheoryArguments(displayName), error, stack, stdOut, stdErr));
        }

        return result;
    }

    private static bool TryGetPassedTestName(string trimmed, out string name)
    {
        name = string.Empty;
        var m = RxVstestPassedLine.Match(trimmed);
        if (!m.Success)
        {
            return false;
        }

        name = StripTrailingTheoryArguments(m.Groups["name"].Value.Trim());
        return !string.IsNullOrWhiteSpace(name);
    }

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

    /// <summary>
    /// xUnit <c>methodDisplay=method</c> prints only the method; Roslyn filters use <c>Ns.Class.Method</c>.
    /// Match FQN containment, or a display suffix bounded by <c>.</c> so <c>OtherMethod</c> does not match <c>Method</c>.
    /// </summary>
    internal static bool TestNameMatchesFilterNeedle(string name, string needle)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        var display = StripTrailingTheoryArguments(name.Trim());
        if (display.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var needleCore = needle.Trim().TrimStart('.');
        if (needleCore.Length == 0)
        {
            return false;
        }

        if (display.Contains(needleCore, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return needleCore.EndsWith(display, StringComparison.OrdinalIgnoreCase)
               && (needleCore.Length == display.Length
                   || needleCore[needleCore.Length - display.Length - 1] == '.');
    }

    private static bool LineContainsFilterNeedle(string trimmed, string needle)
    {
        if (trimmed.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var needleCore = needle.Trim().TrimStart('.');
        return needleCore.Length > 0
               && trimmed.Contains(needleCore, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTestOutcomeLine(string trimmed)
    {
        if (RxVstestPassedLine.IsMatch(trimmed)
            || TryGetFailedTestName(trimmed, out _)
            || trimmed.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var t = trimmed.TrimStart();
        return t.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase)
               && !IsMsBuildNoiseLine(trimmed);
    }

    private static string StripTrailingTheoryArguments(string name)
    {
        var open = name.IndexOf('(');
        return open > 0 ? name[..open].TrimEnd() : name;
    }

    private static bool IsMsBuildNoiseLine(string line) =>
        IsRestoreOrPruneFailureLine(line)
        || line.Contains("MSBuild", StringComparison.OrdinalIgnoreCase);

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

    private static string? ExtractFilterNeedle(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var idx = filter.IndexOf('~');
        if (idx < 0)
        {
            idx = filter.IndexOf('=');
        }

        return idx >= 0 && idx < filter.Length - 1
            ? filter[(idx + 1)..].Trim()
            : filter.Trim();
    }

    private static readonly string[] StdOutEndBanners =
    [
        "Standard Error Messages:",
        "Standard Error:",
        "Debug Trace:",
        "Attachments:",
    ];

    private static readonly string[] StdErrEndBanners =
    [
        "Debug Trace:",
        "Attachments:",
    ];

    private static readonly string[] StdOutStartBanners =
    [
        "Standard Output Messages:",
        "Standard Output:",
    ];

    private static readonly string[] StdErrStartBanners =
    [
        "Standard Error Messages:",
        "Standard Error:",
    ];

    private static void ExtractFailureFields(
        string block,
        out string error,
        out string stack,
        out string stdOut,
        out string stdErr)
    {
        error = string.Empty;
        stack = string.Empty;

        var emIdx = block.IndexOf("Error Message:", StringComparison.OrdinalIgnoreCase);
        var stIdx = block.IndexOf("Stack Trace:", StringComparison.OrdinalIgnoreCase);

        if (emIdx >= 0)
        {
            var bodyStart = emIdx + "Error Message:".Length;
            var bodyEnd = IndexOfEarliest(block, bodyStart, ErrorBodyTerminators);
            if (bodyEnd < 0)
            {
                bodyEnd = block.Length;
            }

            error = NormalizeDetailBody(block.AsSpan(bodyStart, bodyEnd - bodyStart));
        }
        else if (stIdx > 0)
        {
            var firstNl = block.IndexOf('\n');
            if (firstNl >= 0 && firstNl + 1 < stIdx)
            {
                error = NormalizeDetailBody(block.AsSpan(firstNl + 1, stIdx - firstNl - 1));
            }
        }

        if (stIdx >= 0)
        {
            var afterStart = stIdx + "Stack Trace:".Length;
            var afterEnd = IndexOfEarliest(block, afterStart, "Standard Output Messages:", "Standard Output:", "Standard Error Messages:", "Standard Error:", "Debug Trace:", "Attachments:");
            var after = (afterEnd >= 0 ? block[afterStart..afterEnd] : block[afterStart..]).TrimStart();
            stack = TrimStackTrace(after);
        }

        stdOut = ExtractLabeledBlock(block, StdOutStartBanners, StdOutEndBanners);
        stdErr = ExtractLabeledBlock(block, StdErrStartBanners, StdErrEndBanners);
    }

    private static string ExtractLabeledBlock(string block, string[] startBanners, string[] endBanners)
    {
        var bannerIdx = IndexOfEarliest(block, 0, startBanners);
        if (bannerIdx < 0)
        {
            return string.Empty;
        }

        var lineEnd = block.IndexOf('\n', bannerIdx);
        var bodyStart = lineEnd < 0 ? block.Length : lineEnd + 1;
        if (bodyStart >= block.Length)
        {
            return string.Empty;
        }

        var bodyEnd = IndexOfEarliest(block, bodyStart, endBanners);
        if (bodyEnd < 0)
        {
            bodyEnd = block.Length;
        }

        var joined = JoinNonEmptyTrimmedLines(block.AsSpan(bodyStart, bodyEnd - bodyStart));
        if (string.IsNullOrEmpty(joined))
        {
            return string.Empty;
        }

        joined = TruncatedProcessLog.StripTrailingMsBuildOutcome(joined);
        if (string.IsNullOrEmpty(joined))
        {
            return string.Empty;
        }

        var (head, tail) = TestOutputReportOptions.ScaleHeadTail(
            TestOutputReportOptions.ParseStreamCapChars,
            TestOutputReportOptions.DefaultStdOutChars,
            TestOutputReportOptions.DefaultStdOutHeadChars,
            TestOutputReportOptions.DefaultStdOutTailChars);
        return TruncatedProcessLog.TruncateHeadTail(
            joined,
            TestOutputReportOptions.ParseStreamCapChars,
            head,
            tail);
    }

    private static string JoinNonEmptyTrimmedLines(ReadOnlySpan<char> span)
    {
        var s = span.ToString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in s.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var t = line.TrimEnd();
            if (t.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(t.TrimStart());
        }

        return sb.ToString();
    }

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

    private static string NormalizeDetailBody(ReadOnlySpan<char> span)
    {
        var s = span.ToString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in s.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var t = line.TrimEnd();
            if (t.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(t.TrimStart());
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        return TruncatedProcessLog.BuildTruncatedExcerpt(sb.ToString(), TruncatedProcessLog.DefaultMaxCombinedCharacters);
    }

    private static string TrimStackTrace(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return string.Empty;
        }

        var stackLines = stack
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .Take(MaxStackTraceLinesPerFailure)
            .ToArray();

        var joined = string.Join(Environment.NewLine, stackLines);
        return joined.Length > 1200 ? joined[..1197] + "..." : joined;
    }

    private static void AppendFailureDetails(
        StringBuilder sb,
        ParseResult parse,
        TestOutputReportOptions outputOptions)
    {
        if (parse.BuildError is { } buildError)
        {
            sb.AppendLine($"**MSBuild error:** {EscapeMdBackticks(TruncatedProcessLog.BuildTruncatedExcerpt(buildError))}");
        }

        var failures = parse.Failures;
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

        if (parse.FailureDetailsTruncated)
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

    private sealed record RunSource(string Path, string? Framework)
    {
        internal static RunSource Parse(string text)
        {
            var runtime = text.LastIndexOf(" (", StringComparison.Ordinal);
            if (runtime < 0 || !text.EndsWith(')'))
            {
                return new RunSource(text, null);
            }

            var framework = text[(runtime + 2)..^1];
            const string CorePrefix = ".NETCoreApp,Version=v";
            const string FrameworkPrefix = ".NETFramework,Version=v";
            if (framework.StartsWith(CorePrefix, StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(framework[CorePrefix.Length..], out var version))
            {
                framework = (version.Major >= 5 ? "net" : "netcoreapp") + version;
            }
            else if (framework.StartsWith(FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
            {
                framework = "net" + framework[FrameworkPrefix.Length..].Replace(".", "", StringComparison.Ordinal);
            }

            return new RunSource(text[..runtime], framework);
        }

        internal bool Matches(RunSource compact)
        {
            var name = Path[(Path.AsSpan().LastIndexOfAny('/', '\\') + 1)..];
            return name.Equals(compact.Path, StringComparison.Ordinal)
                && (Framework is null || compact.Framework is null || Framework.Equals(compact.Framework, StringComparison.OrdinalIgnoreCase));
        }
    }

    private readonly record struct LoggerSummary(SummaryBlocks Block, int FirstStart, int LastStart);

    private readonly record struct SummaryBlocks(int BlockCount, int? Total, ReportedTestCounts Counts)
    {
        internal TestSummary? Summary =>
            Total is { } total ? new TestSummary(total, Counts.Passed ?? 0, Counts.Failed ?? 0, Counts.Skipped ?? 0) : null;

        internal SummaryBlocks Add(SummaryBlocks other)
        {
            if (BlockCount == 0 || other.BlockCount == 0)
            {
                return BlockCount == 0 ? other : this;
            }

            return new SummaryBlocks(BlockCount + other.BlockCount, Total + other.Total, Counts.Add(other.Counts));
        }
    }

    private static readonly Regex _individualTestOutcome = new(
        $@"^(?:Passed|Failed|Skipped)\s+.+\s+{VstestDurationBracket}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Display names, skip reasons and filters are user text and may quote an MSBuild diagnostic.
    private static readonly Regex _testLoggerLine = new(
        $@"^\s*(?:\[xUnit\.net[\s\]]|{NoMatchPrefix}\b|(?:Passed|Failed|Skipped)\s+.+\s+{VstestDurationBracket}\s*$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
