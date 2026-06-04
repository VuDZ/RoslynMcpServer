using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Logs compact MCP tool outcomes to the file sink (not a dump of the full tool response).
/// Set <c>ROSLYN_MCP_LOG_TOOL_OUTPUT=full</c> to log entire responses at Information.
/// </summary>
public static class ToolTelemetry
{
    /// <summary>
    /// Logs output metrics, then returns <paramref name="result"/> unchanged.
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
        var estimatedTokens = (length + 3) / 4;
        var summary = ToolLogAnalyzer.BuildSummaryLine(text);

        Log.Information(
            "MCP tool {ToolName} | {Summary} | chars={OutputLengthChars} tokens~={EstimatedTokens}",
            toolName,
            summary,
            length,
            estimatedTokens);

        if (ToolLogAnalyzer.ShouldLogFullOutput())
        {
            if (length > 0)
            {
                Log.Information("MCP tool {ToolName} full output:\n{Body}", toolName, text);
            }

            return;
        }

        foreach (var line in ToolLogAnalyzer.ExtractHighlightLines(text))
        {
            var level = line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failure", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                ? LogEventLevel.Warning
                : LogEventLevel.Information;

            Log.Write(level, "MCP tool {ToolName} | {Highlight}", toolName, line);
        }
    }
}
