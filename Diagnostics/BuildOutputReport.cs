using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Formats build output without changing the process result or the retained full log.</summary>
internal static class BuildOutputReport
{
    internal const string WarningsParameterDescription =
        "Include build warnings. Default false.";

    private const int MaxDiagnostics = 20;

    public static string Format(
        string output,
        int exitCode,
        string context,
        bool includeBuildWarnings,
        string subject = "Build",
        bool timedOut = false,
        bool budgetExhausted = false,
        IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry>? additionalErrors = null,
        bool attachFullReport = true) =>
        Format(
            output, exitCode, context, includeBuildWarnings, out _, subject, timedOut, budgetExhausted, additionalErrors,
            attachFullReport);

    internal static string Format(
        string output,
        int exitCode,
        string context,
        bool includeBuildWarnings,
        out bool succeeded,
        string subject = "Build",
        bool timedOut = false,
        bool budgetExhausted = false,
        IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry>? additionalErrors = null,
        bool attachFullReport = true)
    {
        var diagnostics = ParseDiagnostics(output)
            .Concat(additionalErrors ?? [])
            .DistinctBy(d => (d.Severity.ToLowerInvariant(), d.Code, d.Location, d.Message))
            .ToList();
        var errors = diagnostics.Where(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)).ToList();
        succeeded = exitCode == 0 && errors.Count == 0 && !timedOut && !budgetExhausted;
        var status = timedOut ? "timed out" : budgetExhausted ? "probe budget exhausted" : succeeded ? "succeeded" : "failed";
        var sb = new StringBuilder();
        sb.AppendLine($"## {subject} {status}");
        var visible = errors.Concat(includeBuildWarnings
                ? diagnostics.Where(d => d.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
                : [])
            .ToList();
        if (!succeeded)
        {
            sb.AppendLine();
            sb.AppendLine($"Exit code: `{exitCode}`.");
        }

        AppendDiagnostics(sb, visible);
        if (!succeeded)
        {
            // Filter before taking excerpts: warning floods must not displace the failure evidence.
            var visibleOutput = includeBuildWarnings ? output : WithoutWarnings(output);
            if (errors.Count == 0 || timedOut || budgetExhausted || HasUnparsedErrors(visibleOutput))
            {
                sb.AppendLine();
                sb.AppendLine("Additional process output may explain the failure or incomplete build.");
                TruncatedProcessLog.AppendLastCharacters(sb, "Console output:", visibleOutput);
            }

            MsBuildLogHighlighter.AppendKeyLinesSection(sb, WithoutWarnings(output));
            if (timedOut || budgetExhausted || errors.Count == 0)
            {
                sb.AppendLine(DotNetCliRunner.FormatHangHints(timedOut, cancelled: false));
            }

            sb.AppendLine();
            sb.AppendLine("### Execution context");
            sb.AppendLine(context);
        }

        var text = sb.ToString().TrimEnd();
        return attachFullReport
            ? DiagnosticReportAttachment.AttachToResponse(text, output, !succeeded || visible.Count > MaxDiagnostics)
            : text;
    }

    public static string FormatWarnings(string output)
    {
        var warnings = ParseDiagnostics(output)
            .Where(d => d.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(d => (d.Code, d.Location, d.Message))
            .ToList();
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("### Build warnings");
        sb.AppendLine();
        AppendDiagnostics(sb, warnings);
        return DiagnosticReportAttachment.AttachToResponse(
            sb.ToString().TrimEnd(), output, warnings.Count > MaxDiagnostics);
    }

    /// <summary>Only use on a build log; test and application streams can contain diagnostic-looking user text.</summary>
    public static string WithoutWarnings(string output)
    {
        var sb = new StringBuilder();
        var warningContinuation = false;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            var hasError = ParseDiagnostics(line)
                .Any(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
            var warning = !hasError && (_warningLine.IsMatch(trimmed) || _warningSummary.IsMatch(trimmed));
            if (warning)
            {
                warningContinuation = true;
                continue;
            }

            if (warningContinuation && _diagnosticContinuation.IsMatch(trimmed))
            {
                continue;
            }

            warningContinuation = false;
            sb.AppendLine(line.TrimEnd('\r'));
        }

        return sb.ToString().TrimEnd();
    }

    public static bool IsBuildCommand(string command)
    {
        var verb = command.TrimStart().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(verb, "build", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsErrorDiagnostic(string line)
    {
        if (!line.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = MatchDiagnostic(line.Trim());
        if (!match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // In a mixed test log, a prose label can introduce a quoted diagnostic.
        var location = match.Groups["location"];
        return !location.Success || HasDiagnosticOrigin(match);
    }

    internal static string? GetDiagnosticSeverityForCapture(string line)
    {
        if (!line.Contains("error", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (DotNetBuildDiagnosticParser.IsSectionHeaderLine(trimmed))
        {
            return null;
        }

        var match = MatchDiagnostic(trimmed);
        if (match.Success)
        {
            return match.Groups["severity"].Value.ToLowerInvariant();
        }

        var entries = DotNetBuildDiagnosticParser.Parse(trimmed);
        return entries.Any(item => item.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            ? "error"
            : entries.FirstOrDefault()?.Severity.ToLowerInvariant();
    }

    private static IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> ParseDiagnostics(string output)
    {
        var diagnostics = new List<DotNetBuildDiagnosticParser.DiagnosticEntry>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || DotNetBuildDiagnosticParser.IsSectionHeaderLine(trimmed))
            {
                continue;
            }

            var match = MatchDiagnostic(trimmed);

            if (!match.Success)
            {
                diagnostics.AddRange(DotNetBuildDiagnosticParser.Parse(trimmed));
                continue;
            }

            var code = match.Groups["code"].Success ? match.Groups["code"].Value : "(none)";
            var location = match.Groups["location"].Success
                ? match.Groups["location"].Value.Trim()
                : code.StartsWith("NU", StringComparison.OrdinalIgnoreCase) ? "(nuget)" : "(msbuild)";
            diagnostics.Add(new DotNetBuildDiagnosticParser.DiagnosticEntry(
                match.Groups["severity"].Value, code, location, match.Groups["message"].Value.Trim()));
        }

        return diagnostics;
    }

    private static Match MatchDiagnostic(string trimmed)
    {
        // A bare error can quote a warning that otherwise looks like a location.
        var bare = _bareDiagnostic.Match(trimmed);
        if (IsSupported(bare))
        {
            return bare;
        }

        var located = _locatedDiagnostic.Match(trimmed);
        return IsSupported(located) ? located : Match.Empty;

        // A task may choose any code. Without an MSBuild origin, arbitrary prose like
        // "Expected: error connecting to server: retrying" has the same delimiters.
        // See docs/project-review-2026-11-25/task-45-p1-test-command-failure-cause-hi.md.
        static bool IsSupported(Match match) =>
            match.Success && (!match.Groups["code"].Success
                || _conventionalCode.IsMatch(match.Groups["code"].Value)
                || HasDiagnosticOrigin(match));
    }

    private static bool HasDiagnosticOrigin(Match match) =>
        _diagnosticOrigin.IsMatch(match.Groups["location"].Value.TrimEnd())
        || _projectSuffix.IsMatch(match.Groups["message"].Value);

    private static bool HasUnparsedErrors(string output) =>
        output.Split('\n').Any(line => _errorLine.IsMatch(line)
            && !_errorSummary.IsMatch(line.Trim())
            && ParseDiagnostics(line).Count == 0);

    private static void AppendDiagnostics(
        StringBuilder sb,
        IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        foreach (var diagnostic in diagnostics.Take(MaxDiagnostics))
        {
            sb.AppendLine($"- **{diagnostic.Severity}** `{diagnostic.Code}` `{diagnostic.Location}` — {diagnostic.Message}");
        }

        if (diagnostics.Count > MaxDiagnostics)
        {
            sb.AppendLine($"[!] Showing the first {MaxDiagnostics} of {diagnostics.Count} diagnostics (errors first).");
        }
    }

    private static readonly Regex _bareDiagnostic = new(
        @"^(?:\d+>\s*)?(?::\s*)?(?<severity>error|warning)(?:\s+(?<code>[^:\r\n]+?))?\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _locatedDiagnostic = new(
        @"^(?:\d+>\s*)?(?<location>.+?)\s*:\s*(?<severity>error|warning)(?:\s+(?<code>[^:\r\n]+?))?\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _diagnosticOrigin = new(
        @"(?:\.\w+|\(\d+(?:,\s*\d+)*\))$|^(?:MSBUILD|CSC|VBC|EXEC)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _projectSuffix = new(
        @"\s\[[^\[\]\r\n]*\.\w*proj(?:::[^\]\r\n]*)?\]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _conventionalCode = new(
        @"^[A-Za-z]+\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _errorLine = new(
        @"\berror\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _errorSummary = new(
        @"^\d+\s+Error\(s\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _warningLine = new(
        @"(?:^|:\s*|\d+>\s*)warning(?:\s+[A-Za-z]+\d+)?\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _warningSummary = new(
        @"^\d+\s+Warning\(s\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _diagnosticContinuation = new(
        @"^(?:\d+>)?\s*\[[^\]]+\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
