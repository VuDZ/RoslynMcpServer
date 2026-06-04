namespace RoslynMcpServer.Diagnostics;

/// <summary>Normalized MSBuild discovery row (from <see cref="Microsoft.Build.Locator"/> or tests).</summary>
public readonly record struct MsBuildInstanceCandidate(
    Version Version,
    string Name,
    string MSBuildPath,
    string? VisualStudioRootPath);
