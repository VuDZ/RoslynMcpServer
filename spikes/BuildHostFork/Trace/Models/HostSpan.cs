using System.Text.Json;

namespace BuildHostFork.Trace;

/// <summary>One span of a fork BuildHost trace; <see cref="Data"/> holds the span's extra fields.</summary>
internal sealed record HostSpan(
    int Id,
    int Parent,
    string Kind,
    string? Path,
    long Start,
    long End,
    IReadOnlyDictionary<string, JsonElement> Data)
{
    public string? Text(string name)
        => Data.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public long? Number(string name)
        => Data.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    public bool Flag(string name)
        => Data.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    public IReadOnlyList<string> Texts(string name)
        => Data.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
            : [];
}
