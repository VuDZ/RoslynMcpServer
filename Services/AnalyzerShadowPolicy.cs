namespace RoslynMcpServer.Services;

/// <summary>
/// Epoch-2 main-only generation policy. Dependency-set uses a separate versioned namespace later
/// and must not reuse or extend a main-only generation.
/// </summary>
internal static class AnalyzerShadowPolicy
{
    public const int FormatVersion = 2;
    public const string PolicyName = "main-only";
    public const string LayoutSegment = "v2-main-only";
    public const string ManifestFileName = "manifest.json";
    public const int SourceStabilityRetries = 3;

    public static string GetPolicyRoot(string shadowRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowRootDirectory);
        return Path.Combine(Path.GetFullPath(shadowRootDirectory), LayoutSegment);
    }
}
