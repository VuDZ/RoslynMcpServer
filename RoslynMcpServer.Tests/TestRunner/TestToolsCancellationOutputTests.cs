using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tests.Testing;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestToolsCancellationOutputTests
{
    [Theory]
    [InlineData("dotnet")]
    [InlineData("specific")]
    [InlineData("filter")]
    public async Task Runner_cancellation_preserves_the_existing_response_even_without_a_signalled_request_token(string tool)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var tools = new TestTools(fixture.Manager, NullLogger<TestTools>.Instance,
            (_, _, _, _, _) => throw new OperationCanceledException());

        var response = tool switch
        {
            "dotnet" => await tools.RunDotNetTest(fixture.ProjectPath),
            "specific" => await tools.RunSpecificTest(fixture.ProjectPath, className: "Cases"),
            _ => await tools.RunTestByFilter(fixture.ProjectPath, "FullyQualifiedName~Cases")
        };

        Assert.Equal("`dotnet test` was cancelled." + Environment.NewLine + Environment.NewLine
            + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true), response);
    }
}
