using RoslynMcpServer.Diagnostics.UsageStatistics;
using RoslynMcpServer.Hosting;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageStatisticsServiceTests
{
    [Fact]
    public async Task Busy_snapshots_and_tool_intervals_preserve_concurrent_unfinished_calls()
    {
        using var files = new UsageTestFiles();
        var time = new TestTimeProvider();
        using var usage = new UsageStatisticsService(files.UsageDirectory, timeProvider: time);
        await usage.StartAsync(CancellationToken.None);
        var longCall = usage.BeginTool("long", requiresWorkspace: true);
        time.Advance(TimeSpan.FromMinutes(1));
        var shortCall = usage.BeginTool("get_tool_help", requiresWorkspace: false);
        usage.EndTool(shortCall, success: true);
        time.Advance(TimeSpan.FromMinutes(16));
        usage.SampleMemory();
        usage.SetExitReason("exception");
        await usage.StopAsync(CancellationToken.None);

        var events = files.ReadEvents();
        var busy = events.Last(item => item.GetProperty("ev").GetString() == "mem");
        Assert.Equal(1, busy.GetProperty("active_calls").GetInt32());
        Assert.Equal(0, busy.GetProperty("idle_seconds").GetDouble());
        Assert.Equal(2, events.Count(item => item.GetProperty("ev").GetString() == "tool_start"));
        Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"));
        Assert.DoesNotContain(events, item =>
            item.GetProperty("ev").GetString() == "tool" && item.GetProperty("call").GetInt64() == longCall!.Id);
        Assert.Equal("exception", events.Last(item => item.GetProperty("ev").GetString() == "exit").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Failed_or_oversized_records_leave_sequence_gaps_and_subsequent_calls_work()
    {
        using var files = new UsageTestFiles();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        await usage.StartAsync(CancellationToken.None);
        var oversized = usage.BeginTool(new string('x', 5000), requiresWorkspace: false);
        usage.EndTool(oversized, success: true);
        var valid = usage.BeginTool("get_tool_help", requiresWorkspace: false);
        usage.EndTool(valid, success: true);
        await usage.StopAsync(CancellationToken.None);

        var events = files.ReadEvents();
        Assert.Contains(events, item =>
            item.GetProperty("ev").GetString() == "tool" && item.GetProperty("name").GetString() == "get_tool_help");
        var sequences = events.Select(item => item.GetProperty("seq").GetInt64()).ToArray();
        Assert.Contains(sequences.Zip(sequences.Skip(1)), pair => pair.Second - pair.First > 1);
        Assert.All(Directory.GetFiles(files.UsageDirectory, "usage-*.jsonl").SelectMany(File.ReadAllLines),
            line => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(line) + 1, 1, 4096));
    }

    [Fact]
    public async Task Unavailable_usage_directory_never_fails_a_tool_or_shutdown()
    {
        using var files = new UsageTestFiles();
        File.WriteAllText(files.UsageDirectory, "not a directory");
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        await usage.StartAsync(CancellationToken.None);
        var call = usage.BeginTool("get_tool_help", requiresWorkspace: false);
        usage.EndTool(call, success: true);
        await usage.StopAsync(CancellationToken.None);
        await usage.StopAsync(CancellationToken.None);
        Assert.Equal("not a directory", File.ReadAllText(files.UsageDirectory));
    }

    [Fact]
    public async Task Disabled_collection_creates_no_files()
    {
        using var files = new UsageTestFiles();
        using var usage = new UsageStatisticsService(files.UsageDirectory, enabled: false);
        await usage.StartAsync(CancellationToken.None);
        Assert.Null(usage.BeginTool("get_tool_help", requiresWorkspace: false));
        await usage.StopAsync(CancellationToken.None);
        Assert.False(Directory.Exists(files.UsageDirectory));
    }

    [Theory]
    [InlineData("Error: invalid input", false)]
    [InlineData("Failed to reset workspace: denied", false)]
    [InlineData("Reset was cancelled.", false)]
    [InlineData("## Workspace Load Failed", false)]
    [InlineData("## Build succeeded", true)]
    [InlineData("Workspace cleared.", true)]
    public void Outcome_covers_legacy_text_failures(string text, bool expected) =>
        Assert.Equal(expected, UsageStatisticsFilters.IsSuccessfulText(text));

    private sealed class TestTimeProvider : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
            _timestamp += duration.Ticks;
        }

        private DateTimeOffset _now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;
    }
}
