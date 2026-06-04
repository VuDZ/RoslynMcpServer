using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class ProcessOutputExcerptTests
{
    [Fact]
    public void BuildStderrExcerpt_prefers_tail()
    {
        var text = new string('x', 5000);
        var excerpt = ProcessOutputExcerpt.BuildStderrExcerpt(text, maxCharacters: 200, tailCharacters: 150);
        Assert.Contains("stderr truncated", excerpt, StringComparison.Ordinal);
        Assert.EndsWith(new string('x', 150), excerpt);
    }

    [Fact]
    public void BuildStdoutExcerpt_uses_head_and_tail_when_long()
    {
        var text = new string('a', 10000);
        var excerpt = ProcessOutputExcerpt.BuildStdoutExcerpt(text, maxCharacters: 3000);
        Assert.Contains("...[TRUNCATED]...", excerpt, StringComparison.Ordinal);
    }
}
