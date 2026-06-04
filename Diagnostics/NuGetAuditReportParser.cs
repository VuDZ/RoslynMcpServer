using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Parses <c>dotnet list package --vulnerable --format json</c> into a compact audit report.</summary>
public static class NuGetAuditReportParser
{
    public sealed record VulnerabilityEntry(
        string Severity,
        string? AdvisoryUrl);

    public sealed record PackageAuditEntry(
        string PackageId,
        string ResolvedVersion,
        string ProjectPath,
        string TargetFramework,
        bool IsTransitive,
        IReadOnlyList<VulnerabilityEntry> Vulnerabilities);

    public static IReadOnlyList<PackageAuditEntry> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("projects", out var projects))
        {
            return [];
        }

        var results = new List<PackageAuditEntry>();
        foreach (var project in projects.EnumerateArray())
        {
            if (!project.TryGetProperty("path", out var pathProp))
            {
                continue;
            }

            var projectPath = pathProp.GetString() ?? string.Empty;
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                continue;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                var tfm = framework.TryGetProperty("framework", out var tfmProp)
                    ? tfmProp.GetString() ?? string.Empty
                    : string.Empty;

                CollectPackages(framework, "topLevelPackages", projectPath, tfm, transitive: false, results);
                CollectPackages(framework, "transitivePackages", projectPath, tfm, transitive: true, results);
            }
        }

        return results
            .OrderByDescending(e => SeverityRank(e.Vulnerabilities.FirstOrDefault()?.Severity))
            .ThenBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatMarkdownReport(
        string workspacePath,
        IReadOnlyList<PackageAuditEntry> entries,
        int maxEntries = 40)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## NuGet vulnerability audit");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{workspacePath}`");
        sb.AppendLine($"- **Vulnerable package references:** {entries.Count}");
        sb.AppendLine();

        if (entries.Count == 0)
        {
            sb.AppendLine("No packages with known vulnerabilities were reported by `dotnet list package --vulnerable`.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("| Severity | Package | Version | TFM | Project | Transitive | Advisory |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var entry in entries.Take(maxEntries))
        {
            var vuln = entry.Vulnerabilities.FirstOrDefault();
            var severity = vuln?.Severity ?? "unknown";
            var advisory = vuln?.AdvisoryUrl ?? "—";
            var projectShort = Path.GetFileName(entry.ProjectPath);
            sb.AppendLine(
                $"| {severity} | `{entry.PackageId}` | {entry.ResolvedVersion} | {entry.TargetFramework} | `{projectShort}` | {(entry.IsTransitive ? "yes" : "no")} | {advisory} |");
        }

        if (entries.Count > maxEntries)
        {
            sb.AppendLine();
            sb.AppendLine($"[!] Showing first {maxEntries} of {entries.Count} entries.");
        }

        sb.AppendLine();
        sb.AppendLine(
            "> NU1904 in `dotnet build` may still fail when audit is treated as error. "
            + "This report is separate from compile errors — use `run_dotnet_build` for MSBuild diagnostics.");
        return sb.ToString().TrimEnd();
    }

    private static void CollectPackages(
        JsonElement framework,
        string arrayName,
        string projectPath,
        string tfm,
        bool transitive,
        List<PackageAuditEntry> results)
    {
        if (!framework.TryGetProperty(arrayName, out var packages))
        {
            return;
        }

        foreach (var package in packages.EnumerateArray())
        {
            if (!package.TryGetProperty("id", out var idProp))
            {
                continue;
            }

            var id = idProp.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!package.TryGetProperty("vulnerabilities", out var vulns) || vulns.GetArrayLength() == 0)
            {
                continue;
            }

            var version = package.TryGetProperty("resolvedVersion", out var verProp)
                ? verProp.GetString() ?? "?"
                : "?";

            var vulnerabilities = new List<VulnerabilityEntry>();
            foreach (var v in vulns.EnumerateArray())
            {
                var severity = v.TryGetProperty("severity", out var sev)
                    ? sev.GetString() ?? "unknown"
                    : "unknown";
                string? url = null;
                if (v.TryGetProperty("advisoryurl", out var urlProp))
                {
                    url = urlProp.GetString();
                }
                else if (v.TryGetProperty("advisoryUrl", out var urlProp2))
                {
                    url = urlProp2.GetString();
                }

                vulnerabilities.Add(new VulnerabilityEntry(severity, url));
            }

            results.Add(new PackageAuditEntry(id, version, projectPath, tfm, transitive, vulnerabilities));
        }
    }

    private static int SeverityRank(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "moderate" => 2,
        "low" => 1,
        _ => 0
    };
}
