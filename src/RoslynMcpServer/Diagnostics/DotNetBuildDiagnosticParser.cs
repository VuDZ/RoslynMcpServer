using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Parses MSBuild and NuGet SDK lines from combined <c>dotnet build</c> / <c>dotnet test</c> output.</summary>
public static class DotNetBuildDiagnosticParser
{
    private static readonly Regex MsBuildFileLineDiagnostic = new(
        pattern: @"^(?<loc>.+\(\d+,\s*\d+\))\s*:\s*(?<sev>error|warning)\s+(?<code>\S+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MsBuildProjectLineDiagnostic = new(
        pattern: @"^(?<loc>.+?)\s*:\s*(?<sev>error|warning)\s+(?<code>MSB\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NuGetDiagnosticWithPath = new(
        pattern: @"^(?<loc>.+?)\s*:\s*(?<sev>error|warning)\s+(?<code>NU\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NuGetDiagnosticBare = new(
        pattern: @"^(?<sev>error|warning)\s+(?<code>NU\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NuGetDiagnosticColonPrefixed = new(
        pattern: @"^(?:\d+>\s*)?:\s*(?<sev>error|warning)\s+(?<code>NU\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MsBuildDiagnosticColonPrefixed = new(
        pattern: @"^(?:\d+>\s*)?:\s*(?<sev>error|warning)\s+(?<code>MSB\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MsBuildDiagnosticBare = new(
        pattern: @"^(?<sev>error|warning)\s+(?<code>MSB\d+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NuGetDiagnosticEmbedded = new(
        pattern: @"(?<sev>error|warning)\s+(?<code>NU\d+)\s*:\s*(?<msg>.+)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MsBuildDiagnosticEmbedded = new(
        pattern: @"(?<sev>error|warning)\s+(?<code>MSB\d+)\s*:\s*(?<msg>.+)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record DiagnosticEntry(string Severity, string Code, string Location, string Message);

    public static IReadOnlyList<DiagnosticEntry> Parse(string combinedOutput)
    {
        var list = new List<DiagnosticEntry>();
        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return list;
        }

        foreach (var raw in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || IsSectionHeaderLine(line))
            {
                continue;
            }

            var entry = TryParseLine(line);
            if (entry is not null)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    public static bool OutputSuggestsNuGetAuditFailure(string combinedOutput) =>
        !string.IsNullOrEmpty(combinedOutput)
        && (combinedOutput.Contains("NU190", StringComparison.OrdinalIgnoreCase)
            || combinedOutput.Contains("NU160", StringComparison.OrdinalIgnoreCase)
            || combinedOutput.Contains("NuGet audit", StringComparison.OrdinalIgnoreCase)
            || combinedOutput.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
            || combinedOutput.Contains("vulnerability", StringComparison.OrdinalIgnoreCase));

    public static bool IsSectionHeaderLine(string line) =>
        line.StartsWith("--- dotnet", StringComparison.OrdinalIgnoreCase)
        || (line.StartsWith("---", StringComparison.Ordinal) && line.Contains("(exit ", StringComparison.Ordinal));

    private static DiagnosticEntry? TryParseLine(string line)
    {
        var msBuild = MsBuildFileLineDiagnostic.Match(line);
        if (msBuild.Success)
        {
            return Entry(msBuild);
        }

        var msBuildProject = MsBuildProjectLineDiagnostic.Match(line);
        if (msBuildProject.Success)
        {
            return Entry(msBuildProject);
        }

        var nuGetPath = NuGetDiagnosticWithPath.Match(line);
        if (nuGetPath.Success)
        {
            return Entry(nuGetPath);
        }

        var nuGetColon = NuGetDiagnosticColonPrefixed.Match(line);
        if (nuGetColon.Success)
        {
            return Entry(nuGetColon, "(msbuild)");
        }

        var msbColon = MsBuildDiagnosticColonPrefixed.Match(line);
        if (msbColon.Success)
        {
            return Entry(msbColon, "(msbuild)");
        }

        var nuGetBare = NuGetDiagnosticBare.Match(line);
        if (nuGetBare.Success)
        {
            return Entry(nuGetBare, "(nuget)");
        }

        var msbBare = MsBuildDiagnosticBare.Match(line);
        if (msbBare.Success)
        {
            return Entry(msbBare, "(msbuild)");
        }

        var embeddedNu = NuGetDiagnosticEmbedded.Match(line);
        if (embeddedNu.Success)
        {
            return EmbeddedEntry(line, embeddedNu);
        }

        var embeddedMsb = MsBuildDiagnosticEmbedded.Match(line);
        if (embeddedMsb.Success)
        {
            return EmbeddedEntry(line, embeddedMsb);
        }

        return null;
    }

    private static DiagnosticEntry Entry(Match match, string? defaultLoc = null) =>
        new(
            match.Groups["sev"].Value,
            match.Groups["code"].Value,
            defaultLoc ?? match.Groups["loc"].Value.Trim(),
            match.Groups["msg"].Value.Trim());

    private static DiagnosticEntry EmbeddedEntry(string line, Match match)
    {
        var loc = line[..match.Index].Trim().TrimEnd(':');
        if (string.IsNullOrEmpty(loc))
        {
            loc = match.Groups["code"].Value.StartsWith("NU", StringComparison.OrdinalIgnoreCase)
                ? "(nuget)"
                : "(msbuild)";
        }

        return new DiagnosticEntry(
            match.Groups["sev"].Value,
            match.Groups["code"].Value,
            loc,
            match.Groups["msg"].Value.Trim());
    }
}
