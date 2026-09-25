using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

/// <summary>
/// Selects the encoding for rewriting a source file.
/// </summary>
/// <remarks>
/// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> rewrites a changed document during
/// <c>TryApplyChanges</c> with <c>SourceText.Encoding</c>, and <see cref="Encoding.UTF8"/> carries a UTF-8 BOM
/// preamble. Passing it therefore added a BOM to every file that had none. The complementary write
/// (<c>File.WriteAllText</c> without an encoding) strips a BOM that existed. Both write paths of one operation
/// must agree on the file's current BOM state; only new files fall back to BOM-free UTF-8.
/// </remarks>
public static class SourceTextEncoding
{
    /// <summary>UTF-8 without a BOM; the fallback for a file that does not exist yet.</summary>
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
    /// Encoding that reproduces the BOM state of <paramref name="diskPath"/>, or BOM-free UTF-8 when the file is
    /// absent or its first bytes are not a known BOM.
    /// </summary>
    public static Encoding ForDiskPath(string? diskPath)
    {
        return DetectBomEncoding(diskPath) ?? BomFreeUtf8;
    }

    private static Encoding? DetectBomEncoding(string? diskPath)
    {
        if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
        {
            return null;
        }

        try
        {
            // Share read/write: a concurrent writer must not turn encoding detection into a failure.
            using var stream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> head = stackalloc byte[3];
            var read = stream.Read(head);
            if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF)
            {
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            }

            if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE)
            {
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            }

            if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
