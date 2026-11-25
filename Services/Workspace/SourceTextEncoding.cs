using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Selects the encoding for rewriting a source file.
/// </summary>
/// <remarks>
/// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> rewrites a changed document during
/// <c>TryApplyChanges</c> with <c>SourceText.Encoding</c>, and <see cref="Encoding.UTF8"/> carries a UTF-8 BOM
/// preamble. Passing it therefore added a BOM to every file that had none. The complementary write
/// (<c>File.WriteAllText</c> without an encoding) strips a BOM that existed. Both write paths of one operation
/// must agree on the file's current BOM state.
/// </remarks>
public static class SourceTextEncoding
{
    /// <summary>UTF-8 without a BOM.</summary>
    public static Encoding BomFreeUtf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Encoding for a document rewrite: the existing document wins, then the candidate text (a document added by a
    /// refactoring carries the encoding chosen by its creator), then the BOM state of the file on disk.
    /// </summary>
    public static Encoding ResolveForWrite(SourceText? existing, SourceText? candidate, string? diskPath)
    {
        if (existing?.Encoding is { } existingEncoding)
        {
            return existingEncoding;
        }

        if (candidate?.Encoding is { } candidateEncoding)
        {
            return candidateEncoding;
        }

        return ForDiskPath(diskPath);
    }

    /// <summary>
    /// Preserves the BOM state of an existing file. For a new C# file, a UTF-8 BOM identifies the encoding
    /// to every project that compiles it.
    /// </summary>
    public static Encoding ForDiskPath(string? diskPath)
    {
        // MSBuild can include a new file through globs or links outside the project directory.
        // A BOM takes precedence over every owner's CodePage, including owners discovered on reload.
        // See docs/project-review-2026-11-25/task-38-p1-new-source-project-encoding-hi.md.
        if (WorkspaceDiskPathFilter.IsCSharpSource(diskPath) && !File.Exists(diskPath))
        {
            return Encoding.UTF8;
        }

        return DetectBomEncoding(diskPath) ?? BomFreeUtf8;
    }

    internal static Encoding? DetectBomEncoding(ReadOnlySpan<byte> head)
    {
        // UTF-32 little-endian starts with the UTF-16 little-endian BOM.
        if (head.StartsWith(Encoding.UTF32.Preamble))
        {
            return Encoding.UTF32;
        }

        if (head.StartsWith<byte>([0x00, 0x00, 0xFE, 0xFF]))
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (head.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return Encoding.BigEndianUnicode;
        }

        if (head.StartsWith(Encoding.Unicode.Preamble))
        {
            return Encoding.Unicode;
        }

        return head.StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8 : null;
    }

    internal static Encoding? DetectBomEncoding(string? diskPath)
    {
        if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
        {
            return null;
        }

        try
        {
            // Share read/write: a concurrent writer must not turn encoding detection into a failure.
            using var stream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> head = stackalloc byte[4];
            // A shorter file can still carry a UTF-8 or UTF-16 BOM.
            var read = stream.ReadAtLeast(head, head.Length, false);

            return DetectBomEncoding(head[..read]);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    internal static Encoding WithoutBom(int codePage) => codePage switch
    {
        65001 => BomFreeUtf8,
        1200 or 1201 => new UnicodeEncoding(bigEndian: codePage == 1201, byteOrderMark: false),
        12000 or 12001 => new UTF32Encoding(bigEndian: codePage == 12001, byteOrderMark: false),
        _ => CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage),
    };

    internal static bool IsUnshiftedAscii(string text)
    {
        // ESC/SO/SI can shift ISO-2022 into a different alphabet. HZ and UTF-7 can do so using
        // printable bytes alone, so ask their decoders whether the text actually changes.
        // See docs/project-review-2026-11-25/task-44-p1-source-write-reader-encoding-hi.md.
        if (!IsPlainAscii(text))
        {
            return false;
        }

        if (text.Contains('~') && !ReadsLiterally(CodePagesEncodingProvider.Instance.GetEncoding(52936)))
        {
            return false;
        }

        return !text.Contains('+') || ReadsLiterally(_utf7);

        bool ReadsLiterally(Encoding? encoding) => encoding is null
            || string.Equals(encoding.GetString(Encoding.ASCII.GetBytes(text)), text, StringComparison.Ordinal);
    }

    // ISO 646 reserves these positions for national variants (for example, dollar versus currency sign).
    // The surrounding bytes retain their original interpretation when only invariant positions change.
    internal static bool IsInvariantAscii(ReadOnlySpan<char> text) => IsPlainAscii(text)
        && !text.ContainsAny("#$@[\\]^`{|}~");

    private static bool IsPlainAscii(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is not (>= ' ' and <= '~' or '\r' or '\n' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static Encoding? GetSupportedUtf7()
    {
        try
        {
            return Encoding.GetEncoding(65000);
        }
        catch (NotSupportedException)
        {
            // Modern runtimes disable this code page unless the process explicitly opts in.
            return null;
        }
    }

    private static readonly Encoding? _utf7 = GetSupportedUtf7();
}
