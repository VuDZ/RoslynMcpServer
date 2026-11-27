using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Diagnostics.UsageStatistics.Models;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Utility;

public sealed class FileContentPreviewTests
{
    [Fact]
    public async Task LongSingleLineStopsNearTheCharacterBudget()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 1_000_000));
        using var stream = new CountingStream(bytes);
        using var reader = new StreamReader(stream);

        var result = await FileContentPreview.ReadAsync(reader, CancellationToken.None);

        Assert.True(result.IsTruncated);
        Assert.Equal(new string('x', 32_000), result.Content);
        Assert.InRange(stream.BytesRead, 32_001, 32_000 + 2_048);
    }

    [Fact]
    public async Task ManyLinesStopNearTheLineBudgetAndReturnTheFirstHundred()
    {
        const string line = "line\r\n";
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 25_000)));
        using var stream = new CountingStream(bytes);
        using var reader = new StreamReader(stream);

        var result = await FileContentPreview.ReadAsync(reader, CancellationToken.None);

        Assert.True(result.IsTruncated);
        Assert.Equal(string.Join("\r\n", Enumerable.Repeat("line", 100)), result.Content);
        Assert.InRange(stream.BytesRead, 500 * line.Length + 1, 500 * line.Length + 2_048);
    }

    [Theory]
    [InlineData(31_999, false)]
    [InlineData(32_000, false)]
    [InlineData(32_001, true)]
    public async Task CharacterLimitDistinguishesExactFitFromTruncation(int length, bool isTruncated)
    {
        var source = new string('x', length);
        using var reader = new StringReader(source);

        var result = await FileContentPreview.ReadAsync(reader, CancellationToken.None);

        Assert.Equal(source[..Math.Min(length, 32_000)], result.Content);
        Assert.Equal(isTruncated, result.IsTruncated);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\U0001F332")]
    public async Task CharacterLimitDoesNotSplitATwoCharacterSequence(string sequence)
    {
        var prefix = new string('x', 31_999);
        using var reader = new StringReader(prefix + sequence + "tail");

        var result = await FileContentPreview.ReadAsync(reader, CancellationToken.None);

        Assert.Equal(prefix, result.Content);
        Assert.True(result.IsTruncated);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public async Task LineLimitPreservesExactlyFiveHundredLines(string lineEnding)
    {
        var firstLine = new string('x', 1_023);
        var shortLine = "line" + lineEnding;
        var source = firstLine + lineEnding + string.Concat(Enumerable.Repeat(shortLine, 499));
        using var exactReader = new StringReader(source);
        using var longerReader = new StringReader(source + "next line");

        var exact = await FileContentPreview.ReadAsync(exactReader, CancellationToken.None);
        var longer = await FileContentPreview.ReadAsync(longerReader, CancellationToken.None);

        Assert.Equal(source, exact.Content);
        Assert.False(exact.IsTruncated);
        Assert.Equal(firstLine + lineEnding + string.Join(lineEnding, Enumerable.Repeat("line", 99)), longer.Content);
        Assert.True(longer.IsTruncated);
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    public async Task SmallFilesPreserveDecodedTextIncludingMixedLineEndings(int codePage)
    {
        const string source = "\t\u03b1\r\nsecond\rthird\n\U0001F332";
        var encoding = Encoding.GetEncoding(codePage);
        var preamble = encoding.GetPreamble();
        Assert.NotEmpty(preamble);
        using var stream = new MemoryStream([.. preamble, .. encoding.GetBytes(source)]);
        using var reader = new StreamReader(stream);

        var result = await FileContentPreview.ReadAsync(reader, CancellationToken.None);

        Assert.Equal(source, result.Content);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task CancelsPendingRead()
    {
        using var reader = new PendingReader();
        using var cancellation = new CancellationTokenSource();
        var timeout = TimeSpan.FromSeconds(5);

        var read = FileContentPreview.ReadAsync(reader, cancellation.Token);
        await reader.Entered.WaitAsync(timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(timeout));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ToolRepliesWithBoundedPreviewAndTruncationMarker(bool singleLine)
    {
        var source = singleLine
            ? new string('x', 100_000)
            : string.Concat(Enumerable.Repeat("line\r\n", 600));
        var expectedPrefix = singleLine
            ? new string('x', 32_000)
            : string.Join("\r\n", Enumerable.Repeat("line", 100));

        var (reply, unsuccessful) = await ReadThroughToolAsync(source);

        Assert.StartsWith(
            $"{expectedPrefix}{Environment.NewLine}{Environment.NewLine}[!] File content truncated",
            reply,
            StringComparison.Ordinal);
        Assert.InRange(reply.Length, expectedPrefix.Length + 1, expectedPrefix.Length + 200);
        Assert.Contains("`read_file_range`", reply, StringComparison.Ordinal);
        Assert.False(unsuccessful);
    }

    [Fact]
    public async Task ToolReturnsSmallFileContentVerbatim()
    {
        const string source = "## Workspace Load Failed\r\n\tline\n\u03b1\rend";

        var (reply, unsuccessful) = await ReadThroughToolAsync(source);

        Assert.Equal(source, reply);
        Assert.False(unsuccessful);
    }

    [Fact]
    public async Task ToolReportsCancellationAsUnsuccessful()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var (reply, unsuccessful) = await ReadThroughToolAsync("file content", cancellation.Token);

        Assert.Equal("`get_file_content` was cancelled.", reply);
        Assert.True(unsuccessful);
    }

    private static async Task<(string Reply, bool Unsuccessful)> ReadThroughToolAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetTempFileName();
        var call = new UsageToolCall
        {
            Id = 1,
            Name = "get_file_content",
            Started = TimeProvider.System.GetUtcNow(),
            Timestamp = TimeProvider.System.GetTimestamp(),
        };
        var previousCall = ToolTelemetry.SetUsageCall(call);
        try
        {
            await File.WriteAllTextAsync(path, source, Encoding.Unicode);
            var tools = new RoslynTools(NullLogger<RoslynTools>.Instance, SolutionManagerTestFactory.Create());
            var reply = await tools.GetFileContent(path, cancellationToken);
            return (reply, call.Unsuccessful);
        }
        finally
        {
            ToolTelemetry.SetUsageCall(previousCall);
            File.Delete(path);
        }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    private sealed class PendingReader : TextReader
    {
        public Task Entered => _entered.Task;

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            _entered.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _release.TrySetResult();
            }

            base.Dispose(disposing);
        }

        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
