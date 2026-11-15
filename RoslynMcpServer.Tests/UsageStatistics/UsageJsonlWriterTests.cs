using System.Text;
using System.Text.Json;
using RoslynMcpServer.Diagnostics.UsageStatistics;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageJsonlWriterTests
{
    [Fact]
    public async Task Independent_writers_do_not_interleave_records_or_overrun_rotated_files()
    {
        using var files = new UsageTestFiles();
        var now = DateTimeOffset.UtcNow;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            var writer = new UsageJsonlWriter(files.UsageDirectory, maximumFileBytes: 300);
            for (var index = 0; index < 30; index++)
            {
                var bytes = Encoding.UTF8.GetBytes($"{{\"worker\":{worker},\"index\":{index}}}\n");
                // The writer deliberately gives up after 50 ms; this test checks successful appends under contention.
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    try
                    {
                        writer.Append(bytes, now);
                        break;
                    }
                    catch (IOException ex) when (IsSharingViolation(ex))
                    {
                        await Task.Delay(10, timeout.Token);
                    }
                }
            }
        })));

        var paths = Directory.GetFiles(files.UsageDirectory, "usage-*.jsonl");
        Assert.True(paths.Length > 1);
        Assert.All(paths, path => Assert.InRange(new FileInfo(path).Length, 1, 300));
        var records = paths.SelectMany(File.ReadAllLines).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Equal(120, records.Length);
        Assert.Equal(120, records.Select(record => (
            record.GetProperty("worker").GetInt32(),
            record.GetProperty("index").GetInt32())).Distinct().Count());
    }

    [Fact]
    public async Task A_held_lease_rejects_append_without_a_record_and_allows_retry_after_release()
    {
        using var files = new UsageTestFiles();
        Directory.CreateDirectory(files.UsageDirectory);
        var writer = new UsageJsonlWriter(files.UsageDirectory);
        var now = DateTimeOffset.UtcNow;
        var record = Encoding.UTF8.GetBytes("{}\n");
        using (var lease = new FileStream(
            Path.Combine(files.UsageDirectory, ".writer.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            var failure = await Task.Run(() => Assert.Throws<IOException>(() => writer.Append(record, now)))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(IsSharingViolation(failure), $"Unexpected I/O error: {failure}");
            Assert.Empty(Directory.GetFiles(files.UsageDirectory, "usage-*.jsonl"));
        }

        writer.Append(record, now);

        var path = Assert.Single(Directory.GetFiles(files.UsageDirectory, "usage-*.jsonl"));
        Assert.Equal(record, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void Rotation_uses_utc_day_and_retention_uses_age()
    {
        using var files = new UsageTestFiles();
        Directory.CreateDirectory(files.UsageDirectory);
        var expired = Path.Combine(files.UsageDirectory, "usage-20200101-0.jsonl");
        var recent = Path.Combine(files.UsageDirectory, "usage-20200102-0.jsonl");
        File.WriteAllText(expired, "{}\n");
        File.WriteAllText(recent, "{}\n");
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(expired, now.UtcDateTime.AddDays(-61));
        File.SetLastWriteTimeUtc(recent, now.UtcDateTime.AddDays(-59));
        var writer = new UsageJsonlWriter(files.UsageDirectory);
        writer.Append(Encoding.UTF8.GetBytes("{}\n"), now);
        writer.Append(Encoding.UTF8.GetBytes("{}\n"), now.AddDays(1));

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(recent));
        Assert.Contains(Directory.GetFiles(files.UsageDirectory), path => path.Contains(now.UtcDateTime.ToString("yyyyMMdd")));
        Assert.Contains(Directory.GetFiles(files.UsageDirectory), path => path.Contains(now.AddDays(1).UtcDateTime.ToString("yyyyMMdd")));
    }

    [Fact]
    public void Oversized_records_are_rejected_before_creating_a_directory()
    {
        using var files = new UsageTestFiles();
        var writer = new UsageJsonlWriter(files.UsageDirectory);
        Assert.Throws<InvalidDataException>(() => writer.Append(new byte[4097], DateTimeOffset.UtcNow));
        Assert.False(Directory.Exists(files.UsageDirectory));
    }

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xffff) is 32 or 33
        || (!OperatingSystem.IsWindows() && exception.HResult == 11);
}
