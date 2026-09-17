using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Appends a truncated excerpt of combined process stdout/stderr for LLM-visible diagnostics when structured parsing fails.
/// Long logs use a head+tail strategy so early MSBuild errors and final summary lines both appear.
/// Trailing VSTest/MSBuild outcome lines (<c>Build FAILED</c> / <c>0 Error(s)</c>) are stripped first so the tail is the
/// real diagnostic, not the empty compiler counter that always follows a failed test.
/// </summary>
internal static class TruncatedProcessLog
{
    /// <summary>Return full text when at or below this length.</summary>
    public const int DefaultMaxCombinedCharacters = 3000;

    /// <summary>First segment length when combined output exceeds <see cref="DefaultMaxCombinedCharacters"/>.</summary>
    public const int HeadCharactersWhenTruncated = 1000;

    /// <summary>Last segment length when combined output exceeds <see cref="DefaultMaxCombinedCharacters"/>.</summary>
    public const int TailCharactersWhenTruncated = 1500;

    internal const string MiddleMarker = "\n\n...[MIDDLE LOG TRUNCATED]...\n\n";

    private static readonly Regex MsBuildCountLine = new(
        @"^\d+ (?:Warning|Error)\(s\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Obsolete name: kept for call-site stability; implements head+tail truncation.</summary>
    public static void AppendLastCharacters(
        StringBuilder sb,
        string preambleLine,
        string combinedStdoutStderr,
        int maxCombinedCharacters = DefaultMaxCombinedCharacters)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentException.ThrowIfNullOrEmpty(preambleLine);

        sb.AppendLine();
        sb.AppendLine(preambleLine);
        sb.AppendLine();

        if (string.IsNullOrEmpty(combinedStdoutStderr))
        {
            sb.AppendLine("(No stdout/stderr was captured from the process.)");
            return;
        }

        var excerpt = BuildTruncatedExcerpt(combinedStdoutStderr, maxCombinedCharacters);

        sb.AppendLine("```text");
        sb.AppendLine(excerpt);
        sb.AppendLine("```");
    }

    /// <summary>
    /// Drops a trailing MSBuild/VSTest outcome block so a later tail excerpt is not only
    /// <c>Build FAILED</c> / <c>0 Warning(s)</c> / <c>0 Error(s)</c> / <c>Time Elapsed</c>.
    /// Returns the original text when that block is the entire payload.
    /// </summary>
    public static string StripTrailingMsBuildOutcome(string combined)
    {
        ArgumentNullException.ThrowIfNull(combined);
        if (combined.Length == 0)
        {
            return combined;
        }

        var lines = combined.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var end = lines.Length;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
        {
            end--;
        }

        var stripped = false;
        while (end > 0)
        {
            var trimmed = lines[end - 1].Trim();
            if (trimmed.Length == 0)
            {
                end--;
                continue;
            }

            if (!IsMsBuildOutcomeLine(trimmed))
            {
                break;
            }

            end--;
            stripped = true;
        }

        if (!stripped || end == 0)
        {
            return combined;
        }

        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
        {
            end--;
        }

        if (end == 0)
        {
            return combined;
        }

        return string.Join('\n', lines[..end]).TrimEnd();
    }

    /// <summary>
    /// Strips a trailing MSBuild outcome, then if the remainder is at most
    /// <paramref name="maxCombinedCharacters"/> returns it unchanged.
    /// Otherwise returns the first <see cref="HeadCharactersWhenTruncated"/> characters, a middle marker,
    /// and the last <see cref="TailCharactersWhenTruncated"/> characters.
    /// </summary>
    public static string BuildTruncatedExcerpt(string combined, int maxCombinedCharacters = DefaultMaxCombinedCharacters)
    {
        ArgumentNullException.ThrowIfNull(combined);
        var source = StripTrailingMsBuildOutcome(combined);
        if (source.Length <= maxCombinedCharacters)
        {
            return source;
        }

        var head = source[..HeadCharactersWhenTruncated];
        var tail = source[^TailCharactersWhenTruncated..];
        return string.Concat(head, MiddleMarker, tail);
    }

    /// <summary>
    /// Head+tail by character count without stripping MSBuild footers (VSTest Standard Output blocks).
    /// Returns <paramref name="text"/> unchanged when it is at most <paramref name="maxCharacters"/>.
    /// </summary>
    public static string TruncateHeadTail(string text, int maxCharacters, int headCharacters, int tailCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(headCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(tailCharacters);

        var markerLen = MiddleMarker.Length;
        if (maxCharacters <= markerLen + 2)
        {
            return text[..maxCharacters];
        }

        var available = maxCharacters - markerLen;
        var head = Math.Min(headCharacters, Math.Max(1, available - 1));
        var tail = Math.Min(tailCharacters, available - head);
        if (tail < 1)
        {
            return text[..available];
        }

        return string.Concat(text.AsSpan(0, head), MiddleMarker, text.AsSpan(text.Length - tail));
    }

    internal static bool IsMsBuildOutcomeLine(string trimmedLine)
    {
        if (string.IsNullOrEmpty(trimmedLine))
        {
            return false;
        }

        if (trimmedLine.Equals("Build FAILED.", StringComparison.OrdinalIgnoreCase)
            || trimmedLine.Equals("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || trimmedLine.Equals("Build succeeded.", StringComparison.OrdinalIgnoreCase)
            || trimmedLine.Equals("Build succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MsBuildCountLine.IsMatch(trimmedLine))
        {
            return true;
        }

        return trimmedLine.StartsWith("Time Elapsed", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildPreambleTestFailed(int exitCode) =>
        string.Format(CultureInfo.InvariantCulture,
            "Test run failed (Exit Code {0}). Build or execution error log (truncated; first {1} + last {2} chars when output exceeds {3}):",
            exitCode,
            HeadCharactersWhenTruncated,
            TailCharactersWhenTruncated,
            DefaultMaxCombinedCharacters);

    /// <summary>Use under an existing "## Build failed" heading — avoids repeating the word &quot;Build failed&quot;.</summary>
    public static string BuildPreambleBuildConsoleTail(int exitCode) =>
        string.Format(CultureInfo.InvariantCulture,
            "Console output (exit code {0}; truncated; first {1} + last {2} chars when longer than {3}):",
            exitCode,
            HeadCharactersWhenTruncated,
            TailCharactersWhenTruncated,
            DefaultMaxCombinedCharacters);
}
