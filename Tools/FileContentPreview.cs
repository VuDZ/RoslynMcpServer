using System.Text;

namespace RoslynMcpServer.Tools;

internal static class FileContentPreview
{
    internal const int CharacterLimit = 32_000;
    internal const int LineLimit = 500;
    internal const int TruncatedLineCount = 100;
    private const int ReadBufferSize = 1_024;

    internal static async Task<(string Content, bool IsTruncated)> ReadAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBufferSize];
        var content = new StringBuilder(buffer.Length);
        var completedLines = 0;
        var linePreviewLength = 0;
        var previousCharacter = '\0';

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One extra character distinguishes an exact fit from a truncated file.
            var charactersToRead = Math.Min(buffer.Length, CharacterLimit - content.Length + 1);
            var read = await reader.ReadAsync(buffer.AsMemory(0, charactersToRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return (content.ToString(), false);
            }

            for (var i = 0; i < read; i++)
            {
                var character = buffer[i];
                var continuesCrLf = previousCharacter == '\r' && character == '\n';
                if (completedLines == LineLimit && !continuesCrLf)
                {
                    return (content.ToString(0, linePreviewLength), true);
                }

                if (content.Length == CharacterLimit)
                {
                    if (continuesCrLf || (char.IsHighSurrogate(previousCharacter) && char.IsLowSurrogate(character)))
                    {
                        content.Length--;
                    }

                    return (content.ToString(), true);
                }

                if (character is '\r' or '\n' && !continuesCrLf)
                {
                    completedLines++;
                    if (completedLines == TruncatedLineCount)
                    {
                        linePreviewLength = content.Length;
                    }
                }

                content.Append(character);
                previousCharacter = character;
            }
        }
    }
}
