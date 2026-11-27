using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Paths and project instances a coverage gap applies to.
/// When the affected files cannot be named, <see cref="CoversWholeGraph"/> is set and the
/// uncertainty is the whole loaded graph.
/// </summary>
internal sealed class InputCoverageScope
{
    public static InputCoverageScope WholeGraph { get; } = new(
        coversWholeGraph: true,
        Array.Empty<string>(),
        Array.Empty<ProjectId>());

    public bool CoversWholeGraph { get; }

    public IReadOnlyList<string> Paths { get; }

    public IReadOnlyList<ProjectId> Owners { get; }

    public InputCoverageScope(
        bool coversWholeGraph,
        IReadOnlyList<string> paths,
        IReadOnlyList<ProjectId> owners)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(owners);
        CoversWholeGraph = coversWholeGraph;
        Paths = paths;
        Owners = owners;
    }

    public static InputCoverageScope ForPath(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        return new InputCoverageScope(coversWholeGraph: false, new[] { canonicalPath }, Array.Empty<ProjectId>());
    }
}
