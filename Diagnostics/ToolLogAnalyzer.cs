namespace RoslynMcpServer.Diagnostics;

/// <summary>Parses MCP tool text responses into compact log lines (file sink).</summary>
public static class ToolLogAnalyzer
{
    private static readonly string[] HighlightSubstrings =
    [
        "Failure",
        "error",
        "warning",
        "Exception",
        "Failed",
        "MSB",
        "NU1",
        "NU19",
        "GHSA-",
        "## ",
    ];

    public static string BuildSummaryLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "status=empty";
        }

        if (text.Contains("## Workspace Load Failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("## Build failed", StringComparison.OrdinalIgnoreCase))
        {
            return "status=failed";
        }

        if (text.Contains("Successfully loaded workspace", StringComparison.OrdinalIgnoreCase))
        {
            var projects = CountRegexMatches(text, @"^- .+ \[.+\]$");
            var diagSection = ExtractSectionLines(text, "Workspace diagnostics:");
            return $"status=ok | projects={projects} | workspace_diag_lines={diagSection.Count}";
        }

        if (text.Contains("## Build succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return "status=ok | build";
        }

        if (text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Failed:", StringComparison.OrdinalIgnoreCase))
        {
            return "status=error";
        }

        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (firstLine is { Length: > 80 })
        {
            firstLine = firstLine[..77] + "...";
        }

        return string.IsNullOrEmpty(firstLine) ? "status=ok" : $"status=ok | {firstLine}";
    }

    public static IReadOnlyList<string> ExtractHighlightLines(string text, int maxLines = 30)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var highlights = new List<string>(maxLines);
        var inDiagnostics = false;

        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("Workspace diagnostics:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("### Errors", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Diagnostics:", StringComparison.OrdinalIgnoreCase))
            {
                inDiagnostics = true;
                continue;
            }

            if (inDiagnostics && line.StartsWith('-'))
            {
                AddHighlight(highlights, line, maxLines);
                continue;
            }

            if (line.StartsWith("Successfully loaded", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("- **", StringComparison.Ordinal))
            {
                inDiagnostics = false;
            }

            if (ShouldHighlight(line))
            {
                AddHighlight(highlights, line, maxLines);
            }
        }

        return highlights;
    }

    public static bool ShouldLogFullOutput() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ROSLYN_MCP_LOG_TOOL_OUTPUT"),
            "full",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldHighlight(string line)
    {
        foreach (var token in HighlightSubstrings)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddHighlight(List<string> list, string line, int maxLines)
    {
        if (list.Count >= maxLines || list.Contains(line, StringComparer.Ordinal))
        {
            return;
        }

        list.Add(line);
    }

    private static int CountRegexMatches(string text, string pattern)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(l => System.Text.RegularExpressions.Regex.IsMatch(l.TrimEnd(), pattern));
    }

    private static List<string> ExtractSectionLines(string text, string sectionHeader)
    {
        var lines = new List<string>();
        var inSection = false;
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith(sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (inSection)
            {
                if (line.StartsWith('-'))
                {
                    lines.Add(line);
                }
                else if (lines.Count > 0 && !line.StartsWith('>'))
                {
                    break;
                }
            }
        }

        return lines;
    }
}
