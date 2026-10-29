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
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var writer = new UsageJsonlWriter(files.UsageDirectory, maximumFileBytes: 300);
            for (var index = 0; index < 30; index++)
            {
                var bytes = Encoding.UTF8.GetBytes($"{{\"worker\":{worker},\"index\":{index}}}\n");
                writer.Append(bytes, now);
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
}
