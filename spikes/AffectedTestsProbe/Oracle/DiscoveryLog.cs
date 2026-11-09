using System.Text.Json;

namespace AffectedTestsProbe;

/// <summary>
/// Reads <c>testId</c> to fully qualified name from the same <c>dotnet test --diag</c> log as the TRX.
/// A normal run does not emit <c>TestDiscovery.Completed</c>. Results arrive as
/// <c>TestExecution.StatsChange</c> batches, and the last batch is on <c>TestExecution.Completed</c>
/// (<c>LastRunTests</c>). Reading one message drops the others.
/// </summary>
internal static class DiscoveryLog
{
    public static IReadOnlyList<DiscoveredCase> Read(string text)
    {
        var found = new Dictionary<string, DiscoveredCase>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < text.Length)
        {
            if (!TryReadMessageType(text, index, out var type, out var key, out var afterType))
            {
                break;
            }

            index = afterType;
            if (type is not ("TestExecution.StatsChange" or "TestExecution.Completed"))
            {
                continue;
            }

            var start = text.LastIndexOf('{', key);
            if (start < 0 || SliceObject(text, start) is not { } json)
            {
                continue;
            }

            index = start + json.Length;
            AddResults(json, found);
        }

        return found.Values.ToArray();
    }

    private static void AddResults(string json, Dictionary<string, DiscoveredCase> found)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("Payload", out var payload))
            {
                return;
            }

            if (payload.TryGetProperty("NewTestResults", out var results))
            {
                ReadResults(results, found);
            }

            if (payload.TryGetProperty("LastRunTests", out var last)
                && last.ValueKind == JsonValueKind.Object
                && last.TryGetProperty("NewTestResults", out var lastResults))
            {
                ReadResults(lastResults, found);
            }
        }
    }

    private static void ReadResults(JsonElement results, Dictionary<string, DiscoveredCase> found)
    {
        if (results.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("TestCase", out var testCase))
            {
                continue;
            }

            var id = Text(testCase, "Id");
            var name = Text(testCase, "FullyQualifiedName");
            if (id is null || name is null || found.ContainsKey(id))
            {
                continue;
            }

            found.Add(id, new DiscoveredCase(id, name));
        }
    }

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool TryReadMessageType(string text, int start, out string type, out int key, out int afterType)
    {
        const string marker = "\"MessageType\"";
        key = text.IndexOf(marker, start, StringComparison.Ordinal);
        type = "";
        afterType = text.Length;
        if (key < 0)
        {
            return false;
        }

        var index = key + marker.Length;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length || text[index] != ':')
        {
            afterType = key + marker.Length;
            return true;
        }

        index++;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length || text[index] != '"')
        {
            afterType = index;
            return true;
        }

        var valueStart = index + 1;
        var valueEnd = text.IndexOf('"', valueStart);
        if (valueEnd < 0)
        {
            return false;
        }

        type = text[valueStart..valueEnd];
        afterType = valueEnd + 1;
        return true;
    }

    // Braces inside strings are not object boundaries. Measured names can contain them.
    private static string? SliceObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var index = start; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(index + 1)];
                }
            }
        }

        return null;
    }
}
