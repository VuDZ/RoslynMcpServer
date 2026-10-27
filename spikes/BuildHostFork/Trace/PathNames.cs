namespace BuildHostFork.Trace;

/// <summary>
/// Names paths for a report: relative to the root when the solution may be published, otherwise a stable placeholder
/// that keeps only the extension. A path outside the root gets a placeholder too, even when the solution may be
/// published: a project referenced from outside the repository must not reach a report with its full path. The
/// placeholder map stays next to the private trace and is never published.
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
            // GetRelativePath returns the full path for another drive, and a ".." path for a parent folder.
            var relative = Path.GetRelativePath(root, fullPath);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return relative.Replace('\\', '/');
            }
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
