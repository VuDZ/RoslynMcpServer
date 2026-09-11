using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcpServer.Services;

internal sealed class AnalyzerShadowManifestFile
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

internal sealed class AnalyzerShadowManifestSource
{
    [JsonPropertyName("matchedProjectName")]
    public string? MatchedProjectName { get; set; }

    /// <summary>Diagnostic only. Never a substitute for content validation.</summary>
    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }
}

internal sealed class AnalyzerShadowManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("policy")]
    public string Policy { get; set; } = "";

    [JsonPropertyName("required")]
    public List<AnalyzerShadowManifestFile> Required { get; set; } = new();

    [JsonPropertyName("optional")]
    public List<AnalyzerShadowManifestFile> Optional { get; set; } = new();

    [JsonPropertyName("source")]
    public AnalyzerShadowManifestSource? Source { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}

internal readonly record struct AnalyzerShadowPathSafetyResult(bool Safe, string? Reason);

internal static class AnalyzerShadowPathSafety
{
    public static AnalyzerShadowPathSafetyResult ValidateRelativePath(string? relativePath, string generationRoot)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new AnalyzerShadowPathSafetyResult(false, "empty-relative-path");
        }

        var trimmed = relativePath.Trim();
        if (trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
        {
            return new AnalyzerShadowPathSafetyResult(false, "rooted-relative-path");
        }

        if (Path.IsPathRooted(trimmed))
        {
            return new AnalyzerShadowPathSafetyResult(false, "absolute-path");
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            return new AnalyzerShadowPathSafetyResult(false, "traversal-or-volume");
        }

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(generationRoot, trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new AnalyzerShadowPathSafetyResult(false, "unresolvable-relative-path");
        }

        if (!IsContained(combined, generationRoot))
        {
            return new AnalyzerShadowPathSafetyResult(false, "outside-generation-root");
        }

        try
        {
            var info = new FileInfo(combined);
            var target = info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null && !IsContained(target.FullName, generationRoot))
            {
                return new AnalyzerShadowPathSafetyResult(false, "symlink-outside-generation-root");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AnalyzerShadowPathSafetyResult(false, "link-resolution-failed");
        }

        return new AnalyzerShadowPathSafetyResult(true, null);
    }

    public static bool IsContained(string candidatePath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        string candidateFull;
        string rootFull;
        try
        {
            candidateFull = Path.GetFullPath(candidatePath);
            rootFull = Path.GetFullPath(rootDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSep = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
        var candidateNorm = candidateFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
        return candidateNorm.StartsWith(rootWithSep, comparison)
               || string.Equals(candidateFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   comparison);
    }
}
