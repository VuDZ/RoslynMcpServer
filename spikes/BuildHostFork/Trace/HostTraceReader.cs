using System.Globalization;
using System.Text.Json;

namespace BuildHostFork.Trace;

/// <summary>Reads the <c>buildhost-*.jsonl</c> files a fork BuildHost writes when tracing is on.</summary>
internal static class HostTraceReader
{
    public static IReadOnlyList<HostTrace> ReadAll(string directory)
        => Directory.GetFiles(directory, "buildhost-*.jsonl")
            .Order(StringComparer.Ordinal)
            .Select(Read)
            .ToArray();

    // A host killed mid-line leaves a truncated last line; the spans before it are still valid, and the count of
    // unreadable lines lets the report mark the trace incomplete instead of silently using what is left.
    private static HostTrace Read(string path)
    {
        HostTrace? header = null;
        var spans = new List<HostSpan>();
        var unreadable = 0;
        foreach (var line in File.ReadLines(path))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                unreadable++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.GetProperty("event").GetString() == "start")
                {
                    header = ReadHeader(root);
                }
                else
                {
                    spans.Add(ReadSpan(root));
                }
            }
        }

        if (header is null)
        {
            throw new InvalidDataException($"{path} has no start line.");
        }

        return header with { UnreadableLines = unreadable, Spans = spans.OrderBy(span => span.Start).ToArray() };
    }

    private static HostTrace ReadHeader(JsonElement root)
    {
        var frequency = root.GetProperty("frequency").GetInt64();
        var timestamp = root.GetProperty("timestamp").GetInt64();
        var utc = ReadUtc(root, "utc");
        var processStartUtc = ReadUtc(root, "processStartUtc");
        var sinceStart = (long)((utc - processStartUtc).TotalSeconds * frequency);
        return new HostTrace(root.GetProperty("processId").GetInt32(), frequency, timestamp - sinceStart, timestamp, 0, []);
    }

    private static DateTime ReadUtc(JsonElement root, string name)
        => DateTime.Parse(root.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static HostSpan ReadSpan(JsonElement root)
    {
        var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!_commonFields.Contains(property.Name))
            {
                data[property.Name] = property.Value.Clone();
            }
        }

        return new HostSpan(
            root.GetProperty("id").GetInt32(),
            root.GetProperty("parent").GetInt32(),
            root.GetProperty("kind").GetString()!,
            root.TryGetProperty("path", out var path) ? path.GetString() : null,
            root.GetProperty("start").GetInt64(),
            root.GetProperty("end").GetInt64(),
            data);
    }

    private static readonly HashSet<string> _commonFields = ["event", "id", "parent", "kind", "path", "start", "end", "thread"];
}
