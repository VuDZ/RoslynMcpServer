using System.Text;

namespace AffectedTestsProbe;

internal static class FileBytes
{
    public static async Task WriteTextPreservingBomAsync(
        string path,
        string text,
        byte[] original,
        CancellationToken cancellationToken)
    {
        var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);
        await File.WriteAllTextAsync(path, text, encoding, cancellationToken).ConfigureAwait(false);
    }
}
