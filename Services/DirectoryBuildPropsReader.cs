using System.Text.RegularExpressions;

namespace RoslynMcpServer.Services;

/// <summary>
/// Reads <c>TargetFrameworks</c> / <c>TargetFramework</c> from the nearest <c>Directory.Build.props</c>
/// for <c>load_workspace</c> CrossTargeting guidance (not a full MSBuild evaluation).
/// </summary>
public static class DirectoryBuildPropsReader
{
    public const int DefaultMaxEntries = 10;

    private static readonly Regex TargetFrameworksElement = new(
        @"<TargetFrameworks\s*>(?<value>[^<]+)</TargetFrameworks>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetFrameworkElement = new(
        @"<TargetFramework\s*>(?<value>[^<]+)</TargetFramework>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> ListTargetFrameworks(
        string? workspacePath,
        int maxEntries = DefaultMaxEntries)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(workspacePath))
        {
            return Array.Empty<string>();
        }

        string? dir;
        try
        {
            dir = Path.GetDirectoryName(Path.GetFullPath(workspacePath.Trim()));
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        while (!string.IsNullOrEmpty(dir))
        {
            var propsPath = Path.Combine(dir, "Directory.Build.props");
            if (File.Exists(propsPath))
            {
                try
                {
                    var parsed = Parse(File.ReadAllText(propsPath), maxEntries);
                    if (parsed.Count > 0)
                    {
                        return parsed;
                    }
                }
                catch (IOException)
                {
                    // keep walking
                }
                catch (UnauthorizedAccessException)
                {
                    // keep walking
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Array.Empty<string>();
    }

    internal static IReadOnlyList<string> Parse(string propsXml, int maxEntries = DefaultMaxEntries)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(propsXml))
        {
            return Array.Empty<string>();
        }

        var raw = TryFirstValue(TargetFrameworksElement, propsXml)
                  ?? TryFirstValue(TargetFrameworkElement, propsXml);
        if (raw is null)
        {
            return Array.Empty<string>();
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0 || !seen.Add(part))
            {
                continue;
            }

            found.Add(part);
            if (found.Count >= maxEntries)
            {
                break;
            }
        }

        return found;
    }

    private static string? TryFirstValue(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success)
        {
            return null;
        }

        var v = m.Groups["value"].Value.Trim();
        return v.Length == 0 ? null : v;
    }
}
