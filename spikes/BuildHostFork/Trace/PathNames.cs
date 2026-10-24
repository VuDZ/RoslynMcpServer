namespace BuildHostFork.Trace;

/// <summary>
/// Names paths for a report: relative to the root when the solution may be published, otherwise a stable placeholder
/// that keeps only the extension. The placeholder map stays next to the private trace and is never published.
/// </summary>
internal sealed class PathNames(string root, bool redact)
{
    public IReadOnlyDictionary<string, string> Placeholders => _placeholders;

    public string Name(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(none)";
        }

        var fullPath = Path.GetFullPath(path);
        if (!redact)
        {
            var relative = Path.GetRelativePath(root, fullPath);
            return (relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative).Replace('\\', '/');
        }

        var key = ProjectCosts.PathKey(fullPath);
        if (!_placeholders.TryGetValue(key, out var name))
        {
            name = $"project-{_placeholders.Count + 1:D3}{Path.GetExtension(fullPath)}";
            _placeholders[key] = name;
        }

        return name;
    }

    private readonly Dictionary<string, string> _placeholders = new(StringComparer.Ordinal);
}
