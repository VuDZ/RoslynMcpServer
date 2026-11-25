using System.Text;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Exact bytes a successful text write persists, including the encoding preamble.
/// A hash of those bytes does not identify who wrote them.
/// </summary>
internal static class PersistedPayload
{
    public static byte[] FromText(string text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        var preamble = encoding.Preamble;
        var body = EncodeExactly(text, encoding);
        byte[] payload = preamble.IsEmpty ? body : [.. preamble, .. body];
        // File readers inspect the complete prefix. Leading text can introduce a BOM or turn UTF-16 into UTF-32.
        var detected = SourceTextEncoding.DetectBomEncoding(payload);
        if ((detected?.Preamble.Length ?? 0) != preamble.Length)
        {
            throw new IOException($"The encoded text would change the byte-order mark of the {encoding.WebName} file.");
        }

        return payload;
    }

    private static byte[] EncodeExactly(string text, Encoding encoding)
    {
        // Replacement or best-fit bytes would leave the published Roslyn text different from the file.
        // See docs/project-review-2026-11-25/task-30-p1-patch-unrepresentable-text-med.md.
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        byte[] body;
        try
        {
            body = strict.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            throw new IOException($"The text cannot be represented exactly in {encoding.WebName}.", ex);
        }

        // Legacy code pages can map multiple characters to the same bytes even without using fallback.
        if (encoding is not (UTF8Encoding or UnicodeEncoding or UTF32Encoding)
            && !string.Equals(strict.GetString(body), text, StringComparison.Ordinal))
        {
            throw new IOException($"The text cannot be represented exactly in {encoding.WebName}.");
        }

        return body;
    }
}
