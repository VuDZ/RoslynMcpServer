using Serilog;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Logs MCP tool response size and a bounded preview for context-audit (Serilog file sink).
/// </summary>
public static class ToolTelemetry
{
    private const int TruncatedHeadChars = 250;
    private const int TruncatedTailChars = 250;

    /// <summary>
    /// Logs output metrics at Information level, then returns <paramref name="result"/> unchanged.
    /// </summary>
    public static string TraceAndReturn(string toolName, string result)
    {
        LogOutput(toolName, result);
        return result;
    }

    public static void LogOutput(string toolName, string? result)
    {
        var text = result ?? string.Empty;
        var length = text.Length;
        var estimatedTokens = length / 4;

        string preview;
        if (length < 500)
        {
            preview = text;
        }
        else
        {
            var headLen = Math.Min(TruncatedHeadChars, length);
            var tailLen = Math.Min(TruncatedTailChars, length);
            var head = text[..headLen];
            var tail = tailLen > 0 ? text[^tailLen..] : string.Empty;
            preview = $"{head}\n...[TRUNCATED]...\n{tail}";
        }

        Log.Information(
            "MCP tool {ToolName}: OutputLengthChars={OutputLengthChars}, EstimatedTokens={EstimatedTokens}\n{Preview}",
            toolName,
            length,
            estimatedTokens,
            preview);
    }
}
