using System.Text;

namespace RoslynMcpServer.Services;

/// <summary>Reads log file tails without loading entire multi-GB files; tolerates Serilog file locks.</summary>
public static class LogTailReader
{
    public static string ReadTail(
        string filePath,
        int lastNLines,
        string? filterKeyword,
        CancellationToken cancellationToken)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(filterKeyword);
        var keyword = filterKeyword ?? string.Empty;
        var tail = new Queue<string>(lastNLines);

        foreach (var line in ReadLinesShared(filePath, cancellationToken))
        {
            if (hasFilter && line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (tail.Count == lastNLines)
            {
                _ = tail.Dequeue();
            }

            tail.Enqueue(line);
        }

        return string.Join(Environment.NewLine, tail);
    }

    private static IEnumerable<string> ReadLinesShared(string filePath, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return ReadAllLinesFromSharedStream(filePath, cancellationToken);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        return Array.Empty<string>();
    }

    private static List<string> ReadAllLinesFromSharedStream(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(line);
        }

        return lines;
    }
}
