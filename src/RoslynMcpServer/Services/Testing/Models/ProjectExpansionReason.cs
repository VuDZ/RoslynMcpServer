using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>
/// One reason epoch 1 selected a test project whole, kept as a value so that a consumer decides by
/// <see cref="Kind"/> and <see cref="Owner"/> and never parses <see cref="Text"/>.
/// </summary>
/// <param name="Owner">The project whose input changed; null when the reason is not about one project.</param>
/// <param name="Path">The changed path; null when the reason is not about one path.</param>
internal sealed record ProjectExpansionReason(ProjectExpansionKind Kind, ProjectId? Owner, string? Path)
{
    /// <summary>The line the answer shows for this reason.</summary>
    internal string Text => Kind switch
    {
        ProjectExpansionKind.ProjectGraphFile => "Project graph file changed; reload required.",
        ProjectExpansionKind.ObservationGap =>
            "Observation gap: non-C# inputs may have changed; conservative whole-project expansion.",
        _ => "Project expansion without a call graph: " + Path,
    };
}
