using System.IO.Pipes;
using System.Text;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class RoundtripFixtureTests
{
    [Fact]
    public async Task Output_capture_cancels_with_partial_output_when_the_writer_keeps_the_pipe_open()
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In);
        using var client = new AnonymousPipeClientStream(PipeDirection.Out, pipe.GetClientHandleAsString());
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("Partial build output.");
        Assert.Equal('P', reader.Peek());
        var output = new StringBuilder();
        using var cancellation = new CancellationTokenSource();
        var capture = RoundtripFixture.ReadOutputAsync(reader, output, cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            capture.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Partial build output." + Environment.NewLine, output.ToString());
    }
}
