using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RoslynMcpServer.Services;

/// <summary>Reads solution configuration|platform pairs from <c>.sln</c> / <c>.slnx</c> for agent guidance.</summary>
public static class SolutionConfigurationCatalog
{
    public const int DefaultMaxEntries = 10;

    private static readonly Regex SlnConfigLine = new(
        @"^\s*(?<cfg>[^|=\r\n]+)\|(?<plat>[^=\r\n]+)=",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> ListConfigurationPlatforms(
        string? solutionPath,
        int maxEntries = DefaultMaxEntries)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            return Array.Empty<string>();
        }

        var ext = Path.GetExtension(solutionPath);
        try
        {
            if (ext.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return ListFromSln(File.ReadAllText(solutionPath), maxEntries);
            }

            if (ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return ListFromSlnx(File.ReadAllText(solutionPath), maxEntries);
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    internal static IReadOnlyList<string> ListFromSln(string slnText, int maxEntries = DefaultMaxEntries)
    {
        const string header = "SolutionConfigurationPlatforms";
        var start = slnText.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return Array.Empty<string>();
        }

        var end = slnText.IndexOf("EndGlobalSection", start, StringComparison.OrdinalIgnoreCase);
        var block = end > start ? slnText[start..end] : slnText[start..];
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SlnConfigLine.Matches(block))
        {
            var entry = $"{m.Groups["cfg"].Value.Trim()}|{m.Groups["plat"].Value.Trim()}";
            if (seen.Add(entry))
            {
                found.Add(entry);
                if (found.Count >= maxEntries)
                {
                    break;
                }
            }
        }

        return found;
    }

    internal static IReadOnlyList<string> ListFromSlnx(string slnxText, int maxEntries = DefaultMaxEntries)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(slnxText);
        }
        catch
        {
            return Array.Empty<string>();
        }

        var buildTypes = doc.Descendants()
            .Where(e => e.Name.LocalName.Equals("BuildType", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("Name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var platforms = doc.Descendants()
            .Where(e => e.Name.LocalName.Equals("Platform", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("Name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var found = new List<string>();
        if (buildTypes.Count > 0 && platforms.Count > 0)
        {
            foreach (var cfg in buildTypes)
            {
                foreach (var plat in platforms)
                {
                    found.Add($"{cfg}|{plat}");
                    if (found.Count >= maxEntries)
                    {
                        return found;
                    }
                }
            }

            return found;
        }

        foreach (var cfg in buildTypes)
        {
            found.Add(cfg);
            if (found.Count >= maxEntries)
            {
                return found;
            }
        }

        foreach (var plat in platforms)
        {
            found.Add(plat);
            if (found.Count >= maxEntries)
            {
                return found;
            }
        }

        return found;
    }
}
