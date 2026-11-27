using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Formats <c>load_workspace</c> diagnostic text: full messages plus notes, or a brief category/code summary.
/// </summary>
public static class WorkspaceLoadDiagnosticsReporter
{
    public const int MaxBriefCodes = 8;

    private static readonly Regex RxDiagnosticCode = new(
        @"\b(NU\d+|MSB\d+|NETSDK\d+|ASPDEPR\d+|GHSA-[a-z0-9-]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string FormatSection(IReadOnlyList<string> diagnostics, bool briefOutput)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            return string.Empty;
        }

        return briefOutput ? FormatBrief(diagnostics) : FormatVerbose(diagnostics);
    }

    public static string FormatBrief(IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            return string.Empty;
        }

        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var diagnostic in diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                continue;
            }

            var category = Classify(diagnostic);
            categories[category] = categories.GetValueOrDefault(category) + 1;

            foreach (Match match in RxDiagnosticCode.Matches(diagnostic))
            {
                var code = match.Value.ToUpperInvariant();
                if (code.StartsWith("GHSA-", StringComparison.Ordinal))
                {
                    code = "GHSA-" + code["GHSA-".Length..].ToLowerInvariant();
                }

                codes[code] = codes.GetValueOrDefault(code) + 1;
            }
        }

        var counted = categories.Values.Sum();
        if (counted == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("Workspace diagnostics (brief): ");
        sb.Append(counted);
        sb.Append(counted == 1 ? " warning. " : " warning(s). ");

        var categoryParts = categories
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key} {kv.Value}");
        sb.Append("Categories: ");
        sb.Append(string.Join(", ", categoryParts));
        sb.Append('.');

        if (codes.Count > 0)
        {
            var codeParts = codes
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxBriefCodes)
                .Select(kv => $"{kv.Key}×{kv.Value}");
            sb.Append(" Codes: ");
            sb.Append(string.Join(", ", codeParts));
            sb.Append('.');
        }

        sb.Append(" Pass briefOutput=false for full messages.");
        return sb.ToString();
    }

    public static string FormatVerbose(IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Workspace diagnostics:");
        foreach (var diagnostic in diagnostics)
        {
            sb.AppendLine($"- {diagnostic}");
        }

        if (diagnostics.Any(static d =>
                d.Contains("do not have a version specified", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **Note:** Design-time MSBuild can report missing `PackageReference` versions before `dotnet restore`, "
                + "even when `Version=` is present in the `.csproj` on disk. Run `dotnet restore` at the solution root, "
                + "then `reset_workspace` and `load_workspace`. Set MCP env `ROSLYN_MCP_WORKSPACE` to the repo root "
                + "(where `global.json` lives) so MSBuild.Locator pins the same SDK as `run_dotnet_build`.");
        }

        if (diagnostics.Any(static d =>
                d.Contains("NuGet audit", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **Note:** NuGet audit advisories (GHSA / NU1903) are shown as warnings here; `dotnet build` may still fail with `NU1904` if audit is treated as error. Use `run_dotnet_build` for the exact NU lines.");
        }

        if (diagnostics.Any(static d =>
                d.Contains("NuGet prune", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **Note:** NuGet prune / unused `PackageReference` advisories are shown as warnings; the workspace is usable. Remove unused package references if you want a clean restore graph.");
        }

        if (diagnostics.Any(static d =>
                d.Contains("NuGet compat", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **Note:** NuGet TFM-compat advisories (`NU1701`, netfx package in a netcore/net10 project) are shown as warnings; "
                + "`dotnet build` / Visual Studio usually succeed. Use `run_dotnet_build` for the exact NU lines. "
                + "A mis-targeted project may still have incomplete references in Roslyn — prefer fixing the TFM or package.");
        }

        if (diagnostics.Any(static d =>
                d.Contains("MSBuild design-time", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine(
                "> **Note:** Design-time MSBuild warnings (ASP.NET/SDK deprecation, processor-architecture mismatch, "
                + "analyzer project references) are shown as warnings; the workspace is usable. "
                + "MSBuildWorkspace often wraps them as `Msbuild failed when processing the file` without a `warning XXXX` code. "
                + "Use `run_dotnet_build` for real `error NU|MSB|NETSDK` lines.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Classify(string formatted)
    {
        if (formatted.Contains("Warning (NuGet audit):", StringComparison.OrdinalIgnoreCase))
        {
            return "NuGet audit";
        }

        if (formatted.Contains("Warning (NuGet prune):", StringComparison.OrdinalIgnoreCase))
        {
            return "NuGet prune";
        }

        if (formatted.Contains("Warning (NuGet compat):", StringComparison.OrdinalIgnoreCase))
        {
            return "NuGet compat";
        }

        if (formatted.Contains("Warning (MSBuild design-time):", StringComparison.OrdinalIgnoreCase))
        {
            return "MSBuild design-time";
        }

        return "other";
    }
}
