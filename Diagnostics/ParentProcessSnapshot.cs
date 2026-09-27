namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Immediate parent of this process. <see cref="CommandLine"/> is set only when the parent image is node,
/// and must not be written to the log.
/// </summary>
internal readonly record struct ParentProcessSnapshot(string? ImagePath, string? CommandLine)
{
    internal static ParentProcessSnapshot Empty => new(null, null);
}
