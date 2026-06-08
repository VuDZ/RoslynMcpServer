using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class CodeSkeletonToolsTests
{
    [Fact]
    public async Task GetCodeSkeleton_returns_friendly_message_when_path_missing()
    {
        var tool = new CodeSkeletonTools(NullLogger<CodeSkeletonTools>.Instance);

        var result = await tool.GetCodeSkeleton(path: null);

        Assert.Contains("`path` is required", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decompile_type", result, StringComparison.OrdinalIgnoreCase);
    }
}
