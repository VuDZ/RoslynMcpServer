using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Tools;

/// <summary>
/// Line-ending normalization and whitespace-tolerant matching for text patches.
/// </summary>
internal static class PatchMatchHelper
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(2);

    public static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// If the original text uses CRLF, rewrites LF-only <paramref name="modified"/> to CRLF for writing.
    /// </summary>
    public static string RestorePreferredLineEndings(string originalRaw, string modifiedLf)
    {
        if (string.IsNullOrEmpty(modifiedLf))
        {
            return modifiedLf;
        }

        return originalRaw.Contains("\r\n", StringComparison.Ordinal)
            ? modifiedLf.Replace("\n", "\r\n", StringComparison.Ordinal)
            : modifiedLf;
    }

    public static bool TryFindMatch(
        string sourceNormalized,
        string oldNormalized,
        bool preferExact,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(oldNormalized))
        {
            return false;
        }

        if (preferExact)
        {
            var idx = sourceNormalized.IndexOf(oldNormalized, StringComparison.Ordinal);
            if (idx >= 0)
            {
                start = idx;
                end = idx + oldNormalized.Length;
                return true;
            }
        }

        return TryWhitespaceAgnosticMatch(sourceNormalized, oldNormalized, out start, out end);
    }

    /// <summary>
    /// Splits normalized text into non-whitespace tokens and finds the first span in
    /// <paramref name="sourceNormalized"/> where tokens appear in order separated by whitespace.
    /// </summary>
    public static bool TryWhitespaceAgnosticMatch(
        string sourceNormalized,
        string oldNormalized,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(oldNormalized))
        {
            return false;
        }

        var tokens = Regex.Split(oldNormalized, @"\s+", RegexOptions.CultureInvariant)
            .Where(static t => t.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return false;
        }

        var patternBuilder = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                _ = patternBuilder.Append(@"\s+");
            }

            _ = patternBuilder.Append(Regex.Escape(tokens[i]));
        }

        var pattern = patternBuilder.ToString();
        var re = new Regex(
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            RegexMatchTimeout);

        var m = re.Match(sourceNormalized);
        if (!m.Success)
        {
            return false;
        }

        start = m.Index;
        end = m.Index + m.Length;
        return true;
    }

    public static string? ApplyPatchNormalized(
        string sourceNormalized,
        string oldNormalized,
        string newNormalized,
        bool replaceAll,
        out bool matched)
    {
        matched = false;
        if (string.IsNullOrEmpty(oldNormalized))
        {
            return null;
        }

        if (!replaceAll)
        {
            if (!TryFindMatch(sourceNormalized, oldNormalized, preferExact: true, out var s, out var e))
            {
                return null;
            }

            matched = true;
            return sourceNormalized[..s] + newNormalized + sourceNormalized[e..];
        }

        var result = sourceNormalized;
        while (TryFindMatch(result, oldNormalized, preferExact: true, out var s, out var e))
        {
            matched = true;
            result = result[..s] + newNormalized + result[e..];
        }

        return matched ? result : null;
    }

    /// <summary>
    /// Like <see cref="ApplyPatchNormalized"/> but after exact passes, retries with whitespace-agnostic
    /// matching on the current string until no match (when <paramref name="replaceAll"/>).
    /// </summary>
    public static string? ApplyPatchWithFlexibleFallback(
        string sourceNormalized,
        string oldNormalized,
        string newNormalized,
        bool replaceAll,
        out bool usedFlexible,
        out bool matched)
    {
        matched = false;
        usedFlexible = false;

        var exact = ApplyPatchNormalized(sourceNormalized, oldNormalized, newNormalized, replaceAll, out var exactMatched);
        if (exactMatched)
        {
            matched = true;
            return exact;
        }

        var result = sourceNormalized;
        var any = false;
        if (replaceAll)
        {
            while (TryFindMatch(result, oldNormalized, preferExact: false, out var s, out var e))
            {
                any = true;
                usedFlexible = true;
                result = result[..s] + newNormalized + result[e..];
            }
        }
        else
        {
            if (TryFindMatch(result, oldNormalized, preferExact: false, out var s, out var e))
            {
                any = true;
                usedFlexible = true;
                result = result[..s] + newNormalized + result[e..];
            }
        }

        matched = any;
        return any ? result : null;
    }

    /// <summary>
    /// Builds a short diagnostic for logs when a patch could not be applied.
    /// </summary>
    public static string BuildPatchFailureDiagnostic(string oldNormalized, int maxSnippetChars = 400)
    {
        var tokens = Regex.Split(oldNormalized, @"\s+", RegexOptions.CultureInvariant)
            .Where(static t => t.Length > 0)
            .ToArray();

        var head = oldNormalized.Length <= maxSnippetChars
            ? oldNormalized
            : oldNormalized[..maxSnippetChars] + "…";

        var previewTokens = tokens.Length <= 8
            ? string.Join(" | ", tokens)
            : string.Join(" | ", tokens.Take(8)) + " | …";

        return $"oldLength={oldNormalized.Length}, tokenCount={tokens.Length}, tokens(head)=[{previewTokens}], snippet={head}";
    }
}
