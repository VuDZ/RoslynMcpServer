using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class ToolLogAnalyzerTests
{
    [Fact]
    public void BuildSummaryLine_load_workspace_success()
    {
        const string text = """
            Successfully loaded workspace. Found 5 projects:
            - BrqMover.Domain [Library]

            Workspace diagnostics:
            - Failure: something
            """;

        var summary = ToolLogAnalyzer.BuildSummaryLine(text);
        Assert.Contains("status=ok", summary, StringComparison.Ordinal);
        Assert.Contains("projects=1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractHighlightLines_includes_workspace_diagnostics()
    {
        const string text = """
            Successfully loaded workspace. Found 2 projects:
            - A [Library]

            Workspace diagnostics:
            - Failure: MSBuild could not resolve X
            - Warning: NU1903 Package 'Foo' has a known vulnerability
            """;

        var lines = ToolLogAnalyzer.ExtractHighlightLines(text);
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("Failure", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("NU1903", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractHighlightLines_does_not_use_head_tail_truncation_marker()
    {
        var longMiddle = new string('x', 800);
        var text = $"OK\n{longMiddle}\n- Failure: tail error";
        var lines = ToolLogAnalyzer.ExtractHighlightLines(text);
        Assert.Single(lines);
        Assert.Contains("Failure", lines[0], StringComparison.Ordinal);
    }
}
