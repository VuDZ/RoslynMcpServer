using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Reverse-index entry. Producers (generated items and compilation outputs) are stored
/// separately from consumers (user inputs, additional files, analyzer configs, metadata references).
/// </summary>
internal sealed class InputPathEntry
{
    public string CanonicalPath { get; }

    public bool Present { get; }

    public IReadOnlyList<InputOccurrence> Occurrences { get; }

    public IReadOnlyList<ProjectId> Producers { get; }

    public IReadOnlyList<ProjectId> Consumers { get; }

    public InputPathEntry(string canonicalPath, IReadOnlyList<InputOccurrence> occurrences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentNullException.ThrowIfNull(occurrences);

        CanonicalPath = canonicalPath;
        Occurrences = occurrences;
        Present = occurrences.Any(static occurrence => occurrence.Present);
        Producers = occurrences
            .Where(static occurrence => occurrence.ProducerProjectId is not null)
            .Select(static occurrence => occurrence.ProducerProjectId!)
            .Distinct()
            .ToArray();
        Consumers = occurrences
            .Where(static occurrence => occurrence.Role is InputRole.UserInput
                or InputRole.AdditionalFile
                or InputRole.AnalyzerConfig
                or InputRole.MetadataDependency)
            .Select(static occurrence => occurrence.ProjectId)
            .Distinct()
            .ToArray();
    }
}
