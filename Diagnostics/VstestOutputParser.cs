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

    /// <summary>VSTest console duration: <c>[12 ms]</c>, <c>[1 s]</c>, <c>[1 m 28 s]</c>, <c>[1 h 2 m]</c>.</summary>
    private const string VstestDurationBracket =
        @"\[\d+(?:\.\d+)?\s*(?:ms|s|m|h)(?:\s+\d+(?:\.\d+)?\s*(?:ms|s|m|h))*\]";

    private static readonly Regex RxXunitFailLine = new(
        @"^\[xUnit\.net[^\]]*\]\s+(?<name>.+?)\s+\[FAIL\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxVstestPassedLine = new(
        $@"^\s+Passed\s+(?<name>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]+)+)\s+{VstestDurationBracket}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxVstestFailedLine = new(
        $@"^\s+Failed\s+(?<name>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]+)+)(?:\s+{VstestDurationBracket})?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxNunitFailedLine = new(
        @"^\s*Failed\s*:\s*(?<name>[A-Za-z_][\w]+(?:\.[A-Za-z_][\w]+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxEndSummaryLine = new(
        @"(?<kind>Passed|Failed)!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+)(?:,\s*Skipped:\s*(?<skipped>\d+))?(?:,\s*Total:\s*(?<total>\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxEndSummaryLineAlt = new(
        @"Passed!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxTotalTests = new(
        @"Total tests:\s*(?<total>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>VSTest console block: Total tests + Passed (Failed/Skipped optional). Fail-only .slnx blocks are parsed line-wise.</summary>
    private static readonly Regex RxVstestTotalsBlock = new(
        @"Total tests:\s*(?<total>\d+)(?:[\s\S]{0,2000}?)\s+Passed:\s*(?<passed>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record TestSummary(int Total, int Passed, int Failed, int Skipped);

    public sealed record FailedTestDetail(string Name, string Error, string Stack);

    public sealed record ParseResult(
        TestSummary? Summary,
        bool HasRecognizedSummary,
        bool IsPartialSuccess,
        IReadOnlyList<FailedTestDetail> Failures,
        IReadOnlyList<string> PassedTestNames);

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

    public static ParseResult Parse(string combinedOutput, int exitCode)
    {
        combinedOutput = DeduplicateNuGetAuditLines(combinedOutput);
        var summary = TryParseTestSummary(combinedOutput);
        var hasSummary = summary is not null || HasSummaryMarkers(combinedOutput);
        var passedNames = CollectPassedTestNames(combinedOutput);
        var failures = ParseFailedTestBlocks(combinedOutput, MaxFailedTestDetails);
        var isPartial = exitCode == 0 && !hasSummary;

        return new ParseResult(summary, hasSummary, isPartial, failures, passedNames);
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
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var name in CollectFailedTestNames(combinedOutput))
        {
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var line in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (RxVstestPassedLine.IsMatch(line.TrimEnd())
                && line.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildMarkdownReport(
        ParseResult parse,
        int exitCode,
        string combinedOutput,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch)
    {
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
                AppendFailureDetails(sb, parse.Failures, CountFailureAnchors(combinedOutput) > MaxFailedTestDetails);
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

        var summary = parse.Summary ?? InferSummaryFromMarkers(combinedOutput);
        if (summary is null)
        {
            sb.AppendLine("**Status:** partial");
            sb.AppendLine();
            sb.AppendLine("Tests completed; summary counts could not be parsed.");
            AppendRawTail(sb, combinedOutput);
            return sb.ToString().TrimEnd();
        }

        if (requireFilterMatch && !FilterMatchedAnyTest(filter, combinedOutput, parse.PassedTestNames))
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
            return sb.ToString().TrimEnd();
        }

        var (total, passed, failed, skipped) = summary;

        if (failed == 0 && exitCode == 0)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(filter) ? "## All tests passed successfully!" : "## Filtered tests passed");
            sb.AppendLine();
            sb.AppendLine(
                $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
                (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
            if (parse.PassedTestNames.Count > 0 && !string.IsNullOrWhiteSpace(filter))
            {
                sb.AppendLine();
                sb.AppendLine($"Matched tests: `{EscapeMdBackticks(string.Join("`, `", parse.PassedTestNames.Take(5)))}`");
            }

            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"❌ {failed} Tests Failed.");
        sb.AppendLine();
        sb.AppendLine(
            $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
            (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
        sb.AppendLine();
        AppendFailureDetails(sb, parse.Failures, CountFailureAnchors(combinedOutput) > MaxFailedTestDetails);
        if (parse.Failures.Count == 0)
        {
            sb.AppendLine(
                "_Failure details could not be parsed from the log (format may differ). Inspect raw output below._");
            AppendRawTail(sb, combinedOutput);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendRawTail(StringBuilder sb, string combinedOutput)
    {
        sb.AppendLine();
        sb.AppendLine($"Raw output (last {PartialSuccessTailChars} chars):");
        sb.AppendLine();
        sb.AppendLine("```text");
        if (string.IsNullOrEmpty(combinedOutput))
        {
            sb.AppendLine("(empty)");
        }
        else if (combinedOutput.Length <= PartialSuccessTailChars)
        {
            sb.AppendLine(combinedOutput.TrimEnd());
        }
        else
        {
            sb.AppendLine(combinedOutput[^PartialSuccessTailChars..].TrimEnd());
        }

        sb.AppendLine("```");
    }

    private static bool HasSummaryMarkers(string text) =>
        RxEndSummaryLine.IsMatch(text)
        || RxEndSummaryLineAlt.IsMatch(text)
        || RxTotalTests.IsMatch(text)
        || RxTestRunSuccessful.IsMatch(text)
        || RxVstestTotalsBlock.IsMatch(text);

    private static TestSummary? InferSummaryFromMarkers(string text)
    {
        foreach (Match m in RxEndSummaryLine.Matches(text))
        {
            if (m.Success)
            {
                return SummaryFromEndMatch(m);
            }
        }

        var alt = RxEndSummaryLineAlt.Match(text);
        if (alt.Success)
        {
            var passed = int.Parse(alt.Groups["passed"].Value, CultureInfo.InvariantCulture);
            var failed = int.Parse(alt.Groups["failed"].Value, CultureInfo.InvariantCulture);
            return new TestSummary(passed + failed, passed, failed, 0);
        }

        return TryParseTestSummary(text);
    }

    private static bool IsNuGetAuditLine(string line) =>
        line.Contains("NU190", StringComparison.OrdinalIgnoreCase)
        || line.Contains("NU160", StringComparison.OrdinalIgnoreCase)
        || (line.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
            && line.Contains("warning", StringComparison.OrdinalIgnoreCase));

    private static TestSummary? TryParseTestSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match? lastEnd = null;
        foreach (Match m in RxEndSummaryLine.Matches(text))
        {
            lastEnd = m;
        }

        if (lastEnd is { Success: true })
        {
            return SummaryFromEndMatch(lastEnd);
        }

        var alt = RxEndSummaryLineAlt.Match(text);
        if (alt.Success)
        {
            var passed = int.Parse(alt.Groups["passed"].Value, CultureInfo.InvariantCulture);
            var failed = int.Parse(alt.Groups["failed"].Value, CultureInfo.InvariantCulture);
            return new TestSummary(passed + failed, passed, failed, 0);
        }

        var vstestBlock = RxVstestTotalsBlock.Match(text);
        if (vstestBlock.Success)
        {
            var total = int.Parse(vstestBlock.Groups["total"].Value, CultureInfo.InvariantCulture);
            var passed = int.Parse(vstestBlock.Groups["passed"].Value, CultureInfo.InvariantCulture);
            var failed = TryReadCountAfterTotalTests(text, RxFailedCountLine) ?? 0;
            var skipped = TryReadCountAfterTotalTests(text, RxSkippedCountLine) ?? 0;
            return new TestSummary(total, passed, failed, skipped);
        }

        if (RxTestRunSuccessful.IsMatch(text))
        {
            var fromLines = TryParseVstestCountsFromLines(text);
            if (fromLines is not null)
            {
                return fromLines;
            }
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var tm = RxTotalTests.Match(lines[i].Trim());
            if (!tm.Success)
            {
                continue;
            }

            var summary = TryParseCountsNearTotalTestsLine(lines, i);
            if (summary is not null)
            {
                return summary;
            }
        }

        return null;
    }

    private static TestSummary? TryParseVstestCountsFromLines(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!RxTotalTests.IsMatch(lines[i].Trim()))
            {
                continue;
            }

            return TryParseCountsNearTotalTestsLine(lines, i);
        }

        return null;
    }

    private static TestSummary? TryParseCountsNearTotalTestsLine(string[] lines, int totalTestsLineIndex)
    {
        var tm = RxTotalTests.Match(lines[totalTestsLineIndex].Trim());
        if (!tm.Success)
        {
            return null;
        }

        var total = int.Parse(tm.Groups["total"].Value, CultureInfo.InvariantCulture);
        int? passed = null;
        int? failed = null;
        int? skipped = null;

        for (var j = totalTestsLineIndex; j < Math.Min(totalTestsLineIndex + 24, lines.Length); j++)
        {
            var line = lines[j].TrimEnd();
            var pm = RxPassedCountLine.Match(line);
            if (pm.Success)
            {
                passed = int.Parse(pm.Groups["n"].Value, CultureInfo.InvariantCulture);
            }

            var fm = RxFailedCountLine.Match(line);
            if (fm.Success)
            {
                failed = int.Parse(fm.Groups["n"].Value, CultureInfo.InvariantCulture);
            }

            var sm = RxSkippedCountLine.Match(line);
            if (sm.Success)
            {
                skipped = int.Parse(sm.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
        }

        // .slnx / MSBuild VSTest target often omits Passed: when every test failed (and Failed: on all-pass).
        // Only infer Passed when Failed or Skipped was present — Total-only stays unparsed.
        if (passed is null)
        {
            if (failed is null && skipped is null)
            {
                return null;
            }

            passed = Math.Max(0, total - (failed ?? 0) - (skipped ?? 0));
        }

        failed ??= 0;
        skipped ??= 0;
        return new TestSummary(total, passed.Value, failed.Value, skipped.Value);
    }

    private static int? TryReadCountAfterTotalTests(string text, Regex lineRegex)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = lineRegex.Match(line.TrimEnd());
            if (m.Success)
            {
                return int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static TestSummary SummaryFromEndMatch(Match m)
    {
        var passed = int.Parse(m.Groups["passed"].Value, CultureInfo.InvariantCulture);
        var failed = int.Parse(m.Groups["failed"].Value, CultureInfo.InvariantCulture);
        var skipped = m.Groups["skipped"].Success
            ? int.Parse(m.Groups["skipped"].Value, CultureInfo.InvariantCulture)
            : 0;
        var total = m.Groups["total"].Success
            ? int.Parse(m.Groups["total"].Value, CultureInfo.InvariantCulture)
            : passed + failed + skipped;
        return new TestSummary(total, passed, failed, skipped);
    }

    private static IReadOnlyList<string> CollectPassedTestNames(string text)
    {
        var names = new List<string>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = RxVstestPassedLine.Match(line.TrimEnd());
            if (m.Success)
            {
                names.Add(m.Groups["name"].Value.Trim());
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

    private static IReadOnlyList<FailedTestDetail> ParseFailedTestBlocks(string text, int maxCount)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var blocks = new List<(int StartLine, string Name)>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (TryGetFailedTestName(lines[i].TrimEnd(), out var name))
            {
                blocks.Add((i, name));
            }
        }

        var result = new List<FailedTestDetail>();
        for (var b = 0; b < blocks.Count && result.Count < maxCount; b++)
        {
            var start = blocks[b].StartLine;
            var name = blocks[b].Name;
            var end = b + 1 < blocks.Count ? blocks[b + 1].StartLine : lines.Length;
            var blockText = string.Join(Environment.NewLine, lines[start..end]);
            ExtractErrorAndStack(blockText, out var error, out var stack);
            result.Add(new FailedTestDetail(name, error, stack));
        }

        return result;
    }

    private static bool TryGetFailedTestName(string trimmed, out string name)
    {
        name = string.Empty;
        if (IsMsBuildNoiseLine(trimmed))
        {
            return false;
        }

        var xm = RxXunitFailLine.Match(trimmed);
        if (xm.Success)
        {
            name = xm.Groups["name"].Value.Trim();
            return IsPlausibleTestName(name);
        }

        var nm = RxNunitFailedLine.Match(trimmed);
        if (nm.Success)
        {
            name = nm.Groups["name"].Value.Trim();
            return IsPlausibleTestName(name);
        }

        var vm = RxVstestFailedLine.Match(trimmed);
        if (vm.Success)
        {
            name = vm.Groups["name"].Value.Trim();
            return IsPlausibleTestName(name);
        }

        return false;
    }

    private static bool IsMsBuildNoiseLine(string line) =>
        line.Contains("prune package", StringComparison.OrdinalIgnoreCase)
        || line.Contains("to load", StringComparison.OrdinalIgnoreCase)
        || line.Contains("MSBuild", StringComparison.OrdinalIgnoreCase)
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

    private static void ExtractErrorAndStack(string block, out string error, out string stack)
    {
        error = string.Empty;
        stack = string.Empty;

        var emIdx = block.IndexOf("Error Message:", StringComparison.OrdinalIgnoreCase);
        var stIdx = block.IndexOf("Stack Trace:", StringComparison.OrdinalIgnoreCase);

        if (emIdx >= 0)
        {
            var bodyStart = emIdx + "Error Message:".Length;
            var bodyEnd = stIdx >= 0 ? stIdx : block.Length;
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
            var after = block[(stIdx + "Stack Trace:".Length)..].TrimStart();
            stack = TrimStackTrace(after);
        }
    }

    private static string NormalizeDetailBody(ReadOnlySpan<char> span)
    {
        var s = span.ToString().Trim();
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(t);
        }

        return sb.Length > 512 ? sb.ToString(0, 509) + "..." : sb.ToString();
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

    private static int CountFailureAnchors(string text)
    {
        var n = 0;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryGetFailedTestName(line.TrimEnd(), out _))
            {
                n++;
            }
        }

        return n;
    }

    private static void AppendFailureDetails(StringBuilder sb, IReadOnlyList<FailedTestDetail> failures, bool truncated)
    {
        if (failures.Count == 0)
        {
            return;
        }

        for (var i = 0; i < failures.Count; i++)
        {
            var f = failures[i];
            sb.AppendLine($"{i + 1}. **TestName:** `{EscapeMdBackticks(f.Name)}`");
            if (!string.IsNullOrEmpty(f.Error))
            {
                sb.AppendLine($"   **Error:** {EscapeMdBackticks(f.Error)}");
            }

            if (!string.IsNullOrEmpty(f.Stack))
            {
                var stackOneLine = f.Stack.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
                sb.AppendLine($"   **Stack:** {EscapeMdBackticks(stackOneLine)}");
            }

            sb.AppendLine();
        }

        if (truncated)
        {
            sb.AppendLine("[!] Showing first 5 failures only.");
        }
    }

    private static string EscapeMdBackticks(string s) => s.Replace('`', '\'');
}
