using System.Globalization;
using System.Text;

namespace RoslynMcpServer.Diagnostics.UsageStatistics;

/// <summary>A bounded record append guarded across processes, including rotation and retention.</summary>
internal sealed class UsageJsonlWriter
{
    internal const int MaximumRecordBytes = 4096;
    internal const long DefaultMaximumFileBytes = 32 * 1024 * 1024;

    internal UsageJsonlWriter(string directory, long maximumFileBytes = DefaultMaximumFileBytes)
    {
        _directory = directory;
        _maximumFileBytes = maximumFileBytes;
    }

    internal void Append(byte[] record, DateTimeOffset now)
    {
        if (record.Length > MaximumRecordBytes || record.Length > _maximumFileBytes)
        {
            throw new InvalidDataException("Usage record exceeds the size limit.");
        }

        Directory.CreateDirectory(_directory);
        // A short record is not an atomicity guarantee for regular files. The lock also serializes rotation.
        using var lease = AcquireLease();
        var day = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (_day != day)
        {
            DeleteExpiredFiles(now);
            _day = day;
            _fileNumber = FindLastFileNumber(day);
        }

        while (true)
        {
            var path = Path.Combine(_directory, $"usage-{day}-{_fileNumber}.jsonl");
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length + record.Length <= _maximumFileBytes)
            {
                stream.Write(record);
                return;
            }

            _fileNumber++;
        }
    }

    private FileStream AcquireLease()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    Path.Combine(_directory, ".writer.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (started.Elapsed < TimeSpan.FromMilliseconds(50))
            {
                Thread.Sleep(2);
            }
        }
    }

    private int FindLastFileNumber(string day)
    {
        var maximum = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, $"usage-{day}-*.jsonl"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(stem.AsSpan(stem.LastIndexOf('-') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                maximum = Math.Max(maximum, number);
            }
        }

        return maximum;
    }

    private void DeleteExpiredFiles(DateTimeOffset now)
    {
        foreach (var path in Directory.EnumerateFiles(_directory, "usage-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(path) < now.UtcDateTime.AddDays(-60))
            {
                File.Delete(path);
            }
        }
    }

    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private string? _day;
    private int _fileNumber;
}
