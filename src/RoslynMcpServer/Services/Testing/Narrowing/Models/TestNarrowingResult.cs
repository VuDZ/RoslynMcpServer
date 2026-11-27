using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>
/// The answer for every test project epoch 1 selected whole, in solution order. Projects that epoch 1 did not
/// select whole are not here: narrowing never adds a project.
/// </summary>
internal sealed record TestNarrowingResult(IReadOnlyList<TestProjectNarrowing> Projects)
{
    internal static TestNarrowingResult Empty { get; } = new([]);

    /// <summary>Observed changes for direct test plans whose existing filters are kept.</summary>
    internal IReadOnlyList<IlTypeName> DirectChangedTypes { get; init; } = [];

    /// <summary>The answer for a project, or null when epoch 1 did not select it whole.</summary>
    internal TestProjectNarrowing? Find(ProjectId projectId) =>
        Projects.FirstOrDefault(project => project.ProjectId == projectId);
}
