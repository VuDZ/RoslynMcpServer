using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Tools;

/// <summary>
/// Line-ending normalization and whitespace-tolerant matching for text patches.
/// </summary>
internal static class PatchMatchHelper
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Safety cap so a matcher bug cannot grow the buffer forever. Ordinary source files stay far below this.
    /// </summary>
    internal const int MaxReplacements = 50_000;

    public static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Searches for exact matches before trying whitespace-tolerant matches in the remaining source.
    /// Applies the replacements to the original source and skips inserted text on subsequent searches.
    /// </summary>
    public static string? ApplyPatchWithFlexibleFallback(
        string sourceRaw,
        string oldNormalized,
        string newNormalized,
        bool replaceAll,
        out bool usedFlexible,
        out bool matched,
        out int replacementCount,
        CancellationToken cancellationToken = default)
    {
        matched = false;
        usedFlexible = false;
        replacementCount = 0;
        if (string.IsNullOrEmpty(oldNormalized))
        {
            return null;
        }

        var sourceNormalized = NormalizeLineEndings(sourceRaw);
        var replacement = sourceRaw.Contains("\r\n", StringComparison.Ordinal)
            ? newNormalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : newNormalized;
        StringBuilder? result = null;
        Regex? flexiblePattern = null;
        var searchFrom = 0;
        var rawOffset = 0;

        while (searchFrom < sourceNormalized.Length
            && FindNextMatch(sourceNormalized, oldNormalized, searchFrom, ref flexiblePattern) is { } match)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++replacementCount > MaxReplacements)
            {
                throw new InvalidOperationException($"ApplyPatch exceeded {MaxReplacements} replacements (possible matcher loop).");
            }

            // Normalized offsets locate the match. Rewriting the normalized buffer would also
            // change untouched mixed line endings, including newlines inside string literals.
            // See docs/project-review-2026-11-25/task-50-p1-patch-mixed-line-endings-hi.md.
            var rawStart = AdvanceRawOffset(sourceRaw, rawOffset, match.Start - searchFrom);
            var rawEnd = AdvanceRawOffset(sourceRaw, rawStart, match.End - match.Start);
            result ??= new StringBuilder(sourceRaw.Length);
            result.Append(sourceRaw, rawOffset, rawStart - rawOffset);
            if (sourceNormalized.AsSpan(match.Start, match.End - match.Start).SequenceEqual(newNormalized))
            {
                result.Append(sourceRaw, rawStart, rawEnd - rawStart);
            }
            else
            {
                result.Append(replacement);
            }

            rawOffset = rawEnd;
            searchFrom = match.End;
            usedFlexible |= flexiblePattern is not null;
            if (!replaceAll)
            {
                break;
            }
        }

        matched = result is not null;
        return result?.Append(sourceRaw.AsSpan(rawOffset)).ToString();
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

    private static (int Start, int End)? FindNextMatch(
        string sourceNormalized,
        string oldNormalized,
        int searchFrom,
        ref Regex? flexiblePattern)
    {
        if (flexiblePattern is null)
        {
            var exactStart = sourceNormalized.IndexOf(oldNormalized, searchFrom, StringComparison.Ordinal);
            if (exactStart >= 0)
            {
                return (exactStart, exactStart + oldNormalized.Length);
            }

            var tokens = Regex.Split(oldNormalized, @"\s+", RegexOptions.CultureInvariant)
                .Where(static token => token.Length > 0)
                .Select(Regex.Escape);
            var pattern = string.Join(@"\s+", tokens);
            if (pattern.Length == 0)
            {
                return null;
            }

            flexiblePattern = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexMatchTimeout);
        }

        var match = flexiblePattern.Match(sourceNormalized, searchFrom);
        return match.Success ? (match.Index, match.Index + match.Length) : null;
    }

    private static int AdvanceRawOffset(string sourceRaw, int rawOffset, int normalizedLength)
    {
        for (var index = 0; index < normalizedLength; index++)
        {
            if (sourceRaw[rawOffset] == '\r' && rawOffset + 1 < sourceRaw.Length && sourceRaw[rawOffset + 1] == '\n')
            {
                rawOffset++;
            }

            rawOffset++;
        }

        return rawOffset;
    }
}
