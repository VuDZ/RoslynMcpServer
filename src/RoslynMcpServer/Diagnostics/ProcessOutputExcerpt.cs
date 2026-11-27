namespace RoslynMcpServer.Diagnostics;

/// <summary>Head/tail or tail-only excerpts of process streams for LLM-safe tool responses.</summary>
public static class ProcessOutputExcerpt
{
    public const int DefaultMaxStdoutCharacters = 8000;
    public const int DefaultMaxStderrCharacters = 2000;
    public const int DefaultStderrTailCharacters = 1500;
    public const int HeadCharactersWhenTruncated = 1000;
    public const int TailCharactersWhenTruncated = 1500;

    private const string MiddleMarker = "\n...[TRUNCATED]...\n";

    public static string BuildStdoutExcerpt(string text, int maxCharacters = DefaultMaxStdoutCharacters)
    {
        return BuildExcerpt(text, maxCharacters, preferTailOnly: false);
    }

    public static string BuildStderrExcerpt(
        string text,
        int maxCharacters = DefaultMaxStderrCharacters,
        int tailCharacters = DefaultStderrTailCharacters)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxCharacters)
        {
            return text;
        }

        var tailLen = Math.Min(tailCharacters, maxCharacters);
        return string.Concat(
            $"(stderr truncated; showing last {tailLen} of {text.Length} chars)\n",
            text[^tailLen..]);
    }

    public static string BuildExcerpt(string text, int maxCharacters, bool preferTailOnly)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxCharacters)
        {
            return text;
        }

        if (preferTailOnly)
        {
            return text[^maxCharacters..];
        }

        var headLen = Math.Min(HeadCharactersWhenTruncated, maxCharacters / 2);
        var tailLen = Math.Min(TailCharactersWhenTruncated, maxCharacters - headLen);
        return string.Concat(text[..headLen], MiddleMarker, text[^tailLen..]);
    }
}
