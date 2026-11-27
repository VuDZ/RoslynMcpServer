namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>How paths of assembly files given by a caller are compared.</summary>
internal static class IlPaths
{
    /// <summary>
    /// The caller names an assembly by the same path string everywhere, so the comparison only has to follow
    /// the file system: without case on Windows, exact elsewhere.
    /// </summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
