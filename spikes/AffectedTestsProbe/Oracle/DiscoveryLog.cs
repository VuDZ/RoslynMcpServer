using System.Text.Json;

namespace AffectedTestsProbe;

internal static class DiscoveryLog
{
    public static IReadOnlyList<DiscoveredCase> Read(string text)
    {
        const string marker = "\"MessageType\":\"TestDiscovery.Completed\"";
        var index = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return [];
        }

        var start = text.LastIndexOf('{', index);
        if (start < 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(Slice(text, start));
        if (!document.RootElement.TryGetProperty("Payload", out var payload)
            || !payload.TryGetProperty("LastDiscoveredTests", out var cases)
            || cases.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DiscoveredCase>();
        foreach (var item in cases.EnumerateArray())
        {
            var id = item.TryGetProperty("Id", out var idProperty) ? idProperty.GetString() : null;
            var name = item.TryGetProperty("FullyQualifiedName", out var nameProperty) ? nameProperty.GetString() : null;
            if (id is not null && name is not null)
            {
                result.Add(new DiscoveredCase(id, name));
            }
        }

        return result;
    }

    // Measured fully qualified names do not contain raw braces, so a depth scan is enough.
    private static string Slice(string text, int start)
    {
        var depth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(index + 1)];
                }
            }
        }

        return text[start..];
    }
}
