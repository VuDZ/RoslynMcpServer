using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Rebinds one input-map generation onto portable project keys.
/// The generation id stays in the map and is not copied into the snapshot.
/// Walk-up evaluation files are not loaded-graph memberships and are left unbound on purpose.
/// </summary>
internal static class SnapshotOccurrenceBinder
{
    public const string DocumentRoleSource = "workspace-input-map-document-role";

    public const string UnavailableGeneratedFlagSource = "workspace-input-map-generated-flag-unavailable";

    public const string ProjectFileRoleSource = "workspace-input-map-loaded-project-file";

    public const string OutputRoleSource = "workspace-input-map-output";

    public const string MetadataRoleSource = "workspace-input-map-metadata-reference";

    public const string GeneratedProducerSource = "public-document-generated-flag";

    public const string MetadataProducerSource = "unique-output-and-directed-project-binding";

    public static SnapshotInputPath[] Bind(
        WorkspaceInputMap map,
        IReadOnlyList<SnapshotProject> projects,
        IReadOnlyList<SnapshotDocumentBinding> documents,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(keys);

        var consumed = new HashSet<InputOccurrence>(ReferenceEqualityComparer.Instance);
        var paths = new Dictionary<string, List<SnapshotInputOccurrence>>(InputPathCanon.Comparer);
        BindDocuments(map, documents, keys, consumed, paths);
        foreach (var project in projects)
        {
            var projectId = ProjectIdFor(keys, project.Key);
            BindProjectFile(map, project, projectId, consumed, paths);
            BindOutputs(map, project, projectId, consumed, paths);
            BindMetadata(map, project, projectId, projects, keys, consumed, paths);
            BindAnalyzers(project, paths);
        }

        RequireGraphConsumed(map, keys, consumed);
        return paths
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new SnapshotInputPath(pair.Key, pair.Value.ToArray()))
            .ToArray();
    }

    private static void BindDocuments(
        WorkspaceInputMap map,
        IReadOnlyList<SnapshotDocumentBinding> documents,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys,
        HashSet<InputOccurrence> consumed,
        Dictionary<string, List<SnapshotInputOccurrence>> paths)
    {
        foreach (var binding in documents)
        {
            Require(keys.TryGetValue(binding.ProjectId, out var owner), "input-map-project-unbound");
            var entry = map.Find(binding.Document.Path);
            Require(entry is not null, "input-map-occurrence-unbound");
            var matches = entry!.Occurrences.Where(occurrence => occurrence.DocumentId == binding.DocumentId).ToArray();
            Require(matches.Length == 1, "input-map-occurrence-unbound");
            var match = matches[0];
            Require(match.ProjectId == binding.ProjectId, "input-map-project-unbound");
            Require(RoleMatchesKind(match.Role, binding.Document), "document-role-kind-mismatch");
            Require(
                binding.RoleSource is DocumentRoleSource or UnavailableGeneratedFlagSource,
                "document-role-source-unproven");
            var producer = ProducerForDocument(match, binding, owner!);
            Require(consumed.Add(match), "input-map-occurrence-unbound");
            Add(paths, binding.Document.Path, new SnapshotInputOccurrence(
                owner!,
                binding.Document.Kind,
                match.Role.ToString(),
                binding.RoleSource,
                producer.Key,
                producer.Source,
                match.Present));
        }
    }

    private static (SnapshotProjectKey? Key, string? Source) ProducerForDocument(
        InputOccurrence match,
        SnapshotDocumentBinding binding,
        SnapshotProjectKey owner)
    {
        if (match.Role == InputRole.Generated)
        {
            Require(binding.Document.IsGenerated, "document-generated-flag-unproven");
            Require(match.ProducerProjectId == binding.ProjectId, "document-generated-producer-unproven");
            return (owner, GeneratedProducerSource);
        }

        // A suffix or an intermediate directory does not identify a producer.
        // Unknown and every other non-generated role stay without one.
        Require(!binding.Document.IsGenerated, "document-generated-flag-unproven");
        Require(match.ProducerProjectId is null, "unconfirmed-artifact-must-not-have-a-producer");
        return (null, null);
    }

    private static void BindProjectFile(
        WorkspaceInputMap map,
        SnapshotProject project,
        ProjectId projectId,
        HashSet<InputOccurrence> consumed,
        Dictionary<string, List<SnapshotInputOccurrence>> paths)
    {
        var entry = map.Find(project.Key.ProjectPath);
        Require(entry is not null, "input-map-occurrence-unbound");
        var matches = entry!.Occurrences
            .Where(occurrence => occurrence.Role == InputRole.EvaluationInput
                && occurrence.ProjectId == projectId
                && !consumed.Contains(occurrence))
            .ToArray();
        Require(matches.Length == 1, "input-map-occurrence-unbound");
        var match = matches[0];
        Require(match.ProducerProjectId is null, "unconfirmed-artifact-must-not-have-a-producer");
        Require(consumed.Add(match), "input-map-occurrence-unbound");
        Add(paths, project.Key.ProjectPath, new SnapshotInputOccurrence(
            project.Key,
            null,
            InputRole.EvaluationInput.ToString(),
            ProjectFileRoleSource,
            null,
            null,
            match.Present));
    }

    private static void BindOutputs(
        WorkspaceInputMap map,
        SnapshotProject project,
        ProjectId projectId,
        HashSet<InputOccurrence> consumed,
        Dictionary<string, List<SnapshotInputOccurrence>> paths)
    {
        foreach (var entry in map.Paths)
        {
            foreach (var occurrence in entry.Occurrences)
            {
                if (occurrence.Role != InputRole.Output || occurrence.ProjectId != projectId)
                {
                    continue;
                }

                Require(occurrence.ProducerProjectId == projectId, "output-producer-unproven");
                Require(
                    SamePath(entry.CanonicalPath, project.OutputFilePath)
                    || SamePath(entry.CanonicalPath, project.OutputRefFilePath),
                    "output-path-binding-mismatch");
                Require(consumed.Add(occurrence), "input-map-occurrence-unbound");
                Add(paths, entry.CanonicalPath, new SnapshotInputOccurrence(
                    project.Key,
                    null,
                    InputRole.Output.ToString(),
                    OutputRoleSource,
                    project.Key,
                    OutputRoleSource,
                    occurrence.Present));
            }
        }
    }

    private static void BindMetadata(
        WorkspaceInputMap map,
        SnapshotProject project,
        ProjectId projectId,
        IReadOnlyList<SnapshotProject> projects,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys,
        HashSet<InputOccurrence> consumed,
        Dictionary<string, List<SnapshotInputOccurrence>> paths)
    {
        foreach (var reference in project.MetadataReferences)
        {
            var entry = map.Find(reference.Path);
            Require(entry is not null, "input-map-occurrence-unbound");
            var match = entry!.Occurrences.FirstOrDefault(occurrence =>
                occurrence.Role == InputRole.MetadataDependency
                && occurrence.ProjectId == projectId
                && !consumed.Contains(occurrence));
            Require(match is not null, "input-map-occurrence-unbound");
            var producer = MetadataProducer(projects, project, reference.Path);
            if (producer is not null)
            {
                Require(HasOutputOwner(map, reference.Path, producer, keys), "metadata-producer-unproven");
            }

            Require(consumed.Add(match!), "input-map-occurrence-unbound");
            Add(paths, reference.Path, new SnapshotInputOccurrence(
                project.Key,
                null,
                InputRole.MetadataDependency.ToString(),
                MetadataRoleSource,
                producer,
                producer is null ? null : MetadataProducerSource,
                match!.Present));
        }
    }

    private static void BindAnalyzers(
        SnapshotProject project,
        Dictionary<string, List<SnapshotInputOccurrence>> paths)
    {
        foreach (var analyzer in project.AnalyzerReferences)
        {
            Add(paths, analyzer.OriginalPath, new SnapshotInputOccurrence(
                project.Key,
                null,
                "AnalyzerDependency",
                analyzer.IdentitySource,
                null,
                null,
                File.Exists(analyzer.OriginalPath)));
        }
    }

    private static SnapshotProjectKey? MetadataProducer(
        IReadOnlyList<SnapshotProject> projects,
        SnapshotProject consumer,
        string referencePath)
    {
        var owners = projects
            .Where(candidate => SamePath(candidate.OutputFilePath, referencePath)
                || SamePath(candidate.OutputRefFilePath, referencePath))
            .Select(candidate => candidate.Key)
            .Distinct()
            .ToArray();
        if (owners.Length != 1)
        {
            return null;
        }

        return consumer.ProjectReferences.Any(edge => edge.Target == owners[0]) ? owners[0] : null;
    }

    private static bool HasOutputOwner(
        WorkspaceInputMap map,
        string referencePath,
        SnapshotProjectKey producer,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys)
    {
        var entry = map.Find(referencePath);
        return entry is not null && entry.Occurrences.Any(occurrence =>
            occurrence.Role == InputRole.Output
            && keys.TryGetValue(occurrence.ProjectId, out var owner)
            && owner == producer);
    }

    private static void RequireGraphConsumed(
        WorkspaceInputMap map,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys,
        HashSet<InputOccurrence> consumed)
    {
        foreach (var entry in map.Paths)
        {
            foreach (var occurrence in entry.Occurrences)
            {
                if (IsWalkUpEvaluation(entry.CanonicalPath, occurrence, keys))
                {
                    continue;
                }

                Require(consumed.Contains(occurrence), "input-map-occurrence-unbound");
            }
        }
    }

    private static bool IsWalkUpEvaluation(
        string canonicalPath,
        InputOccurrence occurrence,
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys)
    {
        if (occurrence.Role != InputRole.EvaluationInput
            || !keys.TryGetValue(occurrence.ProjectId, out var key))
        {
            return false;
        }

        return !SamePath(canonicalPath, key.ProjectPath);
    }

    private static bool RoleMatchesKind(InputRole role, SnapshotDocument document) => document.Kind switch
    {
        "source" => role is InputRole.UserInput or InputRole.Generated or InputRole.Unknown,
        "additional" => role == InputRole.AdditionalFile,
        "analyzer-config" => role == InputRole.AnalyzerConfig,
        _ => false,
    };

    private static ProjectId ProjectIdFor(
        IReadOnlyDictionary<ProjectId, SnapshotProjectKey> keys,
        SnapshotProjectKey key)
    {
        var matches = keys.Where(pair => pair.Value == key).Select(pair => pair.Key).ToArray();
        Require(matches.Length == 1, "input-map-project-unbound");
        return matches[0];
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && InputPathCanon.Comparer.Equals(left, right);

    private static void Add(
        Dictionary<string, List<SnapshotInputOccurrence>> paths,
        string path,
        SnapshotInputOccurrence occurrence)
    {
        if (!paths.TryGetValue(path, out var occurrences))
        {
            occurrences = [];
            paths.Add(path, occurrences);
        }

        occurrences.Add(occurrence);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
