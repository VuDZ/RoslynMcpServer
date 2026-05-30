using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcpServer.Services;

public static class NuGetSearchResultParser
{
    public sealed record RegistryPackage(
        string Id,
        string LatestStableVersion,
        string Source,
        long? TotalDownloads);

    public static IReadOnlyList<RegistryPackage> ParseSearchJson(string json, bool exactMatch, string query)
    {
        var root = JsonNode.Parse(json);
        if (root is null)
        {
            return [];
        }

        var results = new List<RegistryPackage>();
        var searchResults = root["searchResult"]?.AsArray();
        if (searchResults is null)
        {
            return results;
        }

        foreach (var sourceNode in searchResults)
        {
            if (sourceNode is null)
            {
                continue;
            }

            var sourceName = sourceNode["sourceName"]?.GetValue<string>() ?? "unknown";
            var packages = sourceNode["packages"]?.AsArray();
            if (packages is null)
            {
                continue;
            }

            foreach (var packageNode in packages)
            {
                if (packageNode is null)
                {
                    continue;
                }

                var id = packageNode["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (exactMatch && !string.Equals(id, query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var latestVersion = packageNode["latestVersion"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    continue;
                }

                long? downloads = null;
                if (packageNode["totalDownloads"] is JsonValue downloadsValue &&
                    downloadsValue.TryGetValue(out long downloadCount))
                {
                    downloads = downloadCount;
                }

                results.Add(new RegistryPackage(id, latestVersion, sourceName, downloads));
            }
        }

        return results
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.TotalDownloads ?? 0)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? TryGetLatestStableFromExactMatchJson(string json, string query)
    {
        var root = JsonNode.Parse(json);
        var versions = new List<string>();
        var searchResults = root?["searchResult"]?.AsArray();
        if (searchResults is null)
        {
            return null;
        }

        foreach (var sourceNode in searchResults)
        {
            var packages = sourceNode?["packages"]?.AsArray();
            if (packages is null)
            {
                continue;
            }

            foreach (var packageNode in packages)
            {
                var id = packageNode?["id"]?.GetValue<string>();
                if (!string.Equals(id, query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var version = packageNode?["version"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(version) && !LooksLikePrerelease(version))
                {
                    versions.Add(version);
                }
            }
        }

        return PickHighestVersion(versions);
    }

    private static bool LooksLikePrerelease(string version)
    {
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        return dash >= 0;
    }

    private static string? PickHighestVersion(IReadOnlyList<string> versions)
    {
        if (versions.Count == 0)
        {
            return null;
        }

        return versions
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .MaxBy(v => ParseComparableVersion(v));
    }

    private static (int Major, int Minor, int Build, int Revision, string Raw) ParseComparableVersion(string version)
    {
        var core = version;
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            core = version[..dash];
        }

        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        static int ParsePart(string? s) => int.TryParse(s, out var n) ? n : 0;

        return (
            ParsePart(parts.ElementAtOrDefault(0)),
            ParsePart(parts.ElementAtOrDefault(1)),
            ParsePart(parts.ElementAtOrDefault(2)),
            ParsePart(parts.ElementAtOrDefault(3)),
            version);
    }
}
