using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class TestToolsRunTestByFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunTestByFilter_rejects_empty_filter(string? filter)
    {
        var tools = new TestTools(
            new SolutionManager(NullLogger<SolutionManager>.Instance),
            NullLogger<TestTools>.Instance);

        var result = await tools.RunTestByFilter(
            workspacePath: @"C:\src\App.Tests.csproj",
            filter: filter!);

        Assert.Contains("`filter` is empty", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Path not found", result, StringComparison.Ordinal);
    }
}
