using System.Globalization;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Appends a truncated excerpt of combined process stdout/stderr for LLM-visible diagnostics when structured parsing fails.
/// Long logs use a head+tail strategy so early MSBuild errors and final summary lines both appear.
/// </summary>
internal static class TruncatedProcessLog
{
    /// <summary>Return full text when at or below this length.</summary>
    public const int DefaultMaxCombinedCharacters = 3000;

    /// <summary>First segment length when combined output exceeds <see cref="DefaultMaxCombinedCharacters"/>.</summary>
    public const int HeadCharactersWhenTruncated = 1000;

    /// <summary>Last segment length when combined output exceeds <see cref="DefaultMaxCombinedCharacters"/>.</summary>
    public const int TailCharactersWhenTruncated = 1500;

    private const string MiddleMarker = "\n\n...[MIDDLE LOG TRUNCATED]...\n\n";

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
    /// If <paramref name="combined"/> is at most <paramref name="maxCombinedCharacters"/>, returns it unchanged.
    /// Otherwise returns the first <see cref="HeadCharactersWhenTruncated"/> characters, a middle marker,
    /// and the last <see cref="TailCharactersWhenTruncated"/> characters.
    /// </summary>
    public static string BuildTruncatedExcerpt(string combined, int maxCombinedCharacters = DefaultMaxCombinedCharacters)
    {
        ArgumentNullException.ThrowIfNull(combined);
        if (combined.Length <= maxCombinedCharacters)
        {
            return combined;
        }

        var head = combined[..HeadCharactersWhenTruncated];
        var tail = combined[^TailCharactersWhenTruncated..];
        return string.Concat(head, MiddleMarker, tail);
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
