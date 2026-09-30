using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services;

/// <summary>
/// Builds the input map from a loaded Roslyn <see cref="Solution"/>.
/// Evaluated documents already include SDK globs, links, and conditions for the instances that are actually loaded.
/// This type does not parse item groups and does not start watchers.
/// </summary>
internal static class WorkspaceInputMapBuilder
{
    public static WorkspaceInputMap Build(
        Solution solution,
        Guid generation,
        string? loadedWorkspacePath,
        bool loadGraphComplete)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var instances = CreateInstances(solution);
        var projectDirectories = ProjectDirectories(instances);
        var occurrences = new Dictionary<string, List<InputOccurrence>>(InputPathCanon.Comparer);
        var metadataOnly = new List<string>();

        IndexProjectFiles(instances, occurrences);
        IndexOutputs(solution, occurrences);
        IndexDocuments(solution, occurrences);
        IndexMetadata(solution, occurrences, metadataOnly);
        IndexWalkUp(instances, occurrences);
        IndexWorkspaceFile(loadedWorkspacePath, instances, occurrences);

        var paths = occurrences
            .Select(pair => new InputPathEntry(pair.Key, pair.Value))
            .ToArray();
        var regions = CreateRegions(instances);
        var watchers = CreateWatchers(regions, paths, projectDirectories);
        var multiTarget = MultiTargetCoverage(instances);
        var reasons = CoverageReasons(loadGraphComplete, multiTarget, metadataOnly);
        var categories = CreateCategories(solution, instances, paths, metadataOnly, multiTarget);
        var searchRoots = SolutionManager.ComputeWatchRoots(
            loadedWorkspacePath,
            instances.Select(static instance => instance.FilePath));

        return new WorkspaceInputMap(
            generation,
            loadGraphComplete,
            multiTarget,
            reasons.Length == 0 ? InputCoverage.Complete : InputCoverage.Unknown,
            reasons,
            metadataOnly,
            instances,
            paths,
            regions,
            watchers,
            categories,
            searchRoots);
    }

    private static LoadedProjectInstance[] CreateInstances(Solution solution)
    {
        var instances = new List<LoadedProjectInstance>();
        foreach (var project in solution.Projects)
        {
            var filePath = InputPathCanon.TryCanonicalize(project.FilePath);
            instances.Add(new LoadedProjectInstance(
                project.Id,
                filePath,
                project.Name,
                project.AssemblyName,
                project.ProjectReferences.Select(static reference => reference.ProjectId).ToArray(),
                DeclaredTargetFrameworkEvidence.Read(filePath)));
        }

        return instances.ToArray();
    }

    private static void IndexProjectFiles(
        IReadOnlyList<LoadedProjectInstance> instances,
        Dictionary<string, List<InputOccurrence>> occurrences)
    {
        foreach (var instance in instances)
        {
            Add(occurrences, instance.FilePath, new InputOccurrence(
                instance.Id,
                DocumentId: null,
                InputRole.EvaluationInput,
                ProducerProjectId: null,
                Present: instance.FilePath is not null && File.Exists(instance.FilePath)));
        }
    }

    private static void IndexOutputs(Solution solution, Dictionary<string, List<InputOccurrence>> occurrences)
    {
        foreach (var project in solution.Projects)
        {
            AddOutput(occurrences, project, project.OutputFilePath);
            AddOutput(occurrences, project, project.OutputRefFilePath);
        }
    }

    private static void AddOutput(
        Dictionary<string, List<InputOccurrence>> occurrences,
        Project project,
        string? outputPath)
    {
        var canonical = InputPathCanon.TryCanonicalize(outputPath);
        if (canonical is null)
        {
            return;
        }

        Add(occurrences, canonical, new InputOccurrence(
            project.Id,
            DocumentId: null,
            InputRole.Output,
            ProducerProjectId: project.Id,
            Present: File.Exists(canonical)));
    }

    private static void IndexDocuments(Solution solution, Dictionary<string, List<InputOccurrence>> occurrences)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                AddDocument(occurrences, project.Id, document, sourceDocument: true);
            }

            foreach (var document in project.AdditionalDocuments)
            {
                AddDocument(occurrences, project.Id, document, sourceDocument: false);
            }

            foreach (var document in project.AnalyzerConfigDocuments)
            {
                AddDocument(occurrences, project.Id, document, sourceDocument: false, analyzerConfig: true);
            }
        }
    }

    private static void AddDocument(
        Dictionary<string, List<InputOccurrence>> occurrences,
        ProjectId projectId,
        TextDocument document,
        bool sourceDocument,
        bool analyzerConfig = false)
    {
        var canonical = InputPathCanon.TryCanonicalize(document.FilePath);
        if (canonical is null)
        {
            return;
        }

        var generated = RoslynDocumentBuildSideEffect.TryGetIsGenerated(document);
        var role = RoleForDocument(generated, sourceDocument, analyzerConfig, canonical);
        Add(occurrences, canonical, new InputOccurrence(
            projectId,
            document.Id,
            role,
            role == InputRole.Generated ? projectId : null,
            Present: File.Exists(canonical)));
    }

    private static InputRole RoleForDocument(
        bool? generated,
        bool sourceDocument,
        bool analyzerConfig,
        string canonicalPath)
    {
        if (generated is null)
        {
            return InputRole.Unknown;
        }

        if (generated.Value)
        {
            return InputRole.Generated;
        }

        // Design-time evaluation includes SDK AssemblyInfo and AssemblyAttributes under obj
        // with this flag clear. The flag then does not confirm a producer, and the Compile
        // membership alone must not record those artifacts as user inputs.
        if (sourceDocument && LooksLikeUnconfirmedGeneratedArtifact(canonicalPath))
        {
            return InputRole.Unknown;
        }

        if (analyzerConfig)
        {
            return InputRole.AnalyzerConfig;
        }

        return sourceDocument ? InputRole.UserInput : InputRole.AdditionalFile;
    }

    private static bool LooksLikeUnconfirmedGeneratedArtifact(string canonicalPath)
    {
        var name = Path.GetFileName(canonicalPath);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static void IndexMetadata(
        Solution solution,
        Dictionary<string, List<InputOccurrence>> occurrences,
        List<string> metadataOnly)
    {
        var outputs = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var project in solution.Projects)
        {
            Remember(outputs, project.OutputFilePath);
            Remember(outputs, project.OutputRefFilePath);
        }

        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>())
            {
                var canonical = InputPathCanon.TryCanonicalize(reference.FilePath);
                if (canonical is null)
                {
                    continue;
                }

                Add(occurrences, canonical, new InputOccurrence(
                    project.Id,
                    DocumentId: null,
                    InputRole.MetadataDependency,
                    ProducerProjectId: OutputProducer(occurrences, canonical),
                    Present: File.Exists(canonical)));

                if (outputs.Contains(canonical) || IsSharedFrameworkOrPackage(canonical))
                {
                    continue;
                }

                if (!metadataOnly.Contains(canonical, InputPathCanon.Comparer))
                {
                    metadataOnly.Add(canonical);
                }
            }
        }
    }

    private static ProjectId? OutputProducer(
        Dictionary<string, List<InputOccurrence>> occurrences,
        string canonicalOutput)
    {
        if (!occurrences.TryGetValue(canonicalOutput, out var existing))
        {
            return null;
        }

        return existing.FirstOrDefault(static occurrence => occurrence.Role == InputRole.Output)?.ProducerProjectId;
    }

    private static void IndexWalkUp(
        IReadOnlyList<LoadedProjectInstance> instances,
        Dictionary<string, List<InputOccurrence>> occurrences)
    {
        foreach (var instance in instances)
        {
            if (instance.FilePath is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(instance.FilePath);
            while (!string.IsNullOrEmpty(directory))
            {
                foreach (var name in WalkUpFileNames)
                {
                    var candidate = Path.Combine(directory, name);
                    Add(occurrences, candidate, new InputOccurrence(
                        instance.Id,
                        DocumentId: null,
                        InputRole.EvaluationInput,
                        ProducerProjectId: null,
                        Present: File.Exists(candidate)));
                }

                var parent = Directory.GetParent(directory)?.FullName;
                if (parent is null || InputPathCanon.Comparer.Equals(parent, directory))
                {
                    break;
                }

                directory = parent;
            }
        }
    }

    private static void IndexWorkspaceFile(
        string? loadedWorkspacePath,
        IReadOnlyList<LoadedProjectInstance> instances,
        Dictionary<string, List<InputOccurrence>> occurrences)
    {
        var canonical = InputPathCanon.TryCanonicalize(loadedWorkspacePath);
        if (canonical is null || !IsSolutionFile(canonical))
        {
            return;
        }

        if (instances.Count == 0)
        {
            return;
        }

        Add(occurrences, canonical, new InputOccurrence(
            instances[0].Id,
            DocumentId: null,
            InputRole.EvaluationInput,
            ProducerProjectId: null,
            Present: File.Exists(canonical)));
    }

    private static MembershipRegion[] CreateRegions(IReadOnlyList<LoadedProjectInstance> instances)
    {
        var regions = new List<MembershipRegion>();
        foreach (var instance in instances)
        {
            if (instance.FilePath is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(instance.FilePath);
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            regions.Add(new MembershipRegion(
                directory,
                Recursive: true,
                instance.Id,
                "Project directory of a loaded instance. A new file here is a potential glob item; "
                    + "the public workspace does not include the inclusion or removal rules, so absence "
                    + "from the document index is not a proven exclusion.",
                CompletenessKnown: false));
        }

        return regions.ToArray();
    }

    private static InputWatcherDescriptor[] CreateWatchers(
        IReadOnlyList<MembershipRegion> regions,
        IReadOnlyList<InputPathEntry> paths,
        IReadOnlyList<string> projectDirectories)
    {
        var seeds = new List<WatcherSeed>();
        foreach (var region in regions)
        {
            seeds.Add(new WatcherSeed(
                region.Directory,
                includeSubdirectories: true,
                "Recursive membership region inside a loaded project directory."));
        }

        foreach (var entry in paths)
        {
            if (!NeedsPointSubscription(entry, projectDirectories))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(entry.CanonicalPath);
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var seed = seeds.FirstOrDefault(candidate =>
                !candidate.IncludeSubdirectories
                && InputPathCanon.Comparer.Equals(candidate.Directory, directory));
            if (seed is null)
            {
                seed = new WatcherSeed(
                    directory,
                    includeSubdirectories: false,
                    "Non-recursive point subscription for a file outside a project directory, "
                        + "or for a walk-up props/targets/global.json candidate. "
                        + "The ancestor tree is not watched recursively.");
                seeds.Add(seed);
            }

            seed.PointFilters.Add(entry.CanonicalPath);
            foreach (var occurrence in entry.Occurrences)
            {
                seed.Roles.Add(occurrence.Role);
            }
        }

        return seeds.Select(static seed => seed.ToDescriptor()).ToArray();
    }

    private static bool NeedsPointSubscription(InputPathEntry entry, IReadOnlyList<string> projectDirectories)
    {
        if (entry.Occurrences.Any(static occurrence => occurrence.Role == InputRole.EvaluationInput))
        {
            return true;
        }

        if (HasIntermediateOrOutputSegment(entry.CanonicalPath))
        {
            return true;
        }

        foreach (var directory in projectDirectories)
        {
            if (WorkspaceDiskPathFilter.IsPathUnderDirectory(entry.CanonicalPath, directory, InputPathCanon.Comparison))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasIntermediateOrOutputSegment(string canonicalPath)
    {
        foreach (var segment in canonicalPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static InputCoverage MultiTargetCoverage(IReadOnlyList<LoadedProjectInstance> instances)
    {
        if (instances.Count == 0)
        {
            return InputCoverage.Unknown;
        }

        foreach (var instance in instances)
        {
            var loaded = instances.Count(other =>
                instance.FilePath is not null
                && other.FilePath is not null
                && InputPathCanon.Comparer.Equals(instance.FilePath, other.FilePath));
            if (instance.DeclaredTargetFrameworks.Count != 1 || loaded != 1)
            {
                return InputCoverage.Unknown;
            }
        }

        return InputCoverage.Complete;
    }

    private static string[] CoverageReasons(
        bool loadGraphComplete,
        InputCoverage multiTarget,
        IReadOnlyList<string> metadataOnly)
    {
        var reasons = new List<string>
        {
            InputCoverageReason.ImportsNotInEvaluatedGraph,
            InputCoverageReason.RestoreInputsNotInEvaluatedGraph,
            InputCoverageReason.CustomTasksNotInEvaluatedGraph,
            InputCoverageReason.ExternalGlobCompletenessUnknown,
            InputCoverageReason.NonDocumentXamlAndResourcesUnknown,
            InputCoverageReason.WalkUpImportNotProven,
        };
        if (!loadGraphComplete)
        {
            reasons.Add(InputCoverageReason.LoadIncomplete);
        }

        if (multiTarget != InputCoverage.Complete)
        {
            reasons.Add(InputCoverageReason.UnconfirmedTargetFramework);
        }

        if (metadataOnly.Count > 0)
        {
            reasons.Add(InputCoverageReason.MetadataOnlyDependency);
        }

        return reasons.ToArray();
    }

    private static InputCategoryEvidence[] CreateCategories(
        Solution solution,
        IReadOnlyList<LoadedProjectInstance> instances,
        IReadOnlyList<InputPathEntry> paths,
        IReadOnlyList<string> metadataOnly,
        InputCoverage multiTarget)
    {
        var owners = instances.Select(static instance => instance.Id).ToArray();
        var withDocuments = solution.Projects.Where(static project => project.Documents.Any()).Select(static project => project.Id).ToArray();
        var withAdditional = solution.Projects.Where(static project => project.AdditionalDocuments.Any()).Select(static project => project.Id).ToArray();
        var withAnalyzerConfigs = solution.Projects.Where(static project => project.AnalyzerConfigDocuments.Any()).Select(static project => project.Id).ToArray();
        var withProjectReferences = instances.Where(static instance => instance.ProjectReferenceIds.Count > 0).Select(static instance => instance.Id).ToArray();
        var withMetadata = solution.Projects.Where(static project => project.MetadataReferences.Count > 0).Select(static project => project.Id).ToArray();

        return
        [
            Category(
                InputCategoryKind.CSharp,
                withDocuments,
                "Project.Documents of loaded instances. SDK globs and links are already evaluated into this list.",
                "Change, create, delete, and rename of known documents, plus create of a new file under a project directory.",
                "The document list is not the inclusion rule. A new .cs under a project directory stays a potential member. "
                    + "A .g.cs, AssemblyInfo, or AssemblyAttributes compile item is not a user input. "
                    + "It is generated only when the build-side-effect flag is set; otherwise the role is unknown."),
            Category(
                InputCategoryKind.Xaml,
                OwnersOfExtension(paths, ".xaml"),
                "Evaluated documents and additional documents whose path ends with .xaml.",
                "Change, create, delete, and rename of those evaluated paths.",
                "The extension is not a role. Page and ApplicationDefinition items that are not documents in the loaded graph are unknown."),
            Category(
                InputCategoryKind.Resources,
                OwnersOfResource(paths),
                "Evaluated documents and additional documents with a resource extension (.resx, .resources, .png, .jpg).",
                "Change, create, delete, and rename of those evaluated paths.",
                "The extension is not a role. EmbeddedResource and Content items absent from the loaded graph are unknown."),
            Category(
                InputCategoryKind.AdditionalFiles,
                withAdditional,
                "Project.AdditionalDocuments.",
                "Change, create, delete, and rename of evaluated additional documents.",
                "Additional items that the loader did not surface are unknown. Text sync of these documents is out of scope."),
            Category(
                InputCategoryKind.AnalyzerConfigs,
                withAnalyzerConfigs,
                "Project.AnalyzerConfigDocuments.",
                "Change, create, delete, and rename of evaluated analyzer configs.",
                "Hierarchical discovery of a new .editorconfig beyond the evaluated documents is unknown."),
            new InputCategoryEvidence(
                InputCategoryKind.Imports,
                "Not available. The public workspace does not list evaluated Import items.",
                "Change of an imported file would matter; the file list is not known.",
                owners,
                "Walk-up file names are candidates only. Other imports stay unknown. Completeness is not claimed.",
                ItemsIndexedFromLoadedGraph: false,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.WalkUpProps,
                "Ancestor file names Directory.Build.props, Directory.Build.targets, Directory.Packages.props, and global.json. Existence is recorded; import is not evaluated.",
                "Create, change, delete, and rename of each candidate, including a file that is currently absent.",
                owners,
                "Each candidate uses a non-recursive point subscription on its own directory. A missing file is evidence, not proof that no props will appear.",
                ItemsIndexedFromLoadedGraph: paths.Any(static entry => entry.Occurrences.Any(static occurrence => occurrence.Role == InputRole.EvaluationInput)),
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.RestoreInputs,
                "Not available. project.assets.json, packages.lock.json, and nuget.config are not project documents.",
                "Change of a restore input would matter; the paths are not indexed as a complete set.",
                owners,
                "Restore inputs stay unknown. They are not inferred from bin or obj.",
                ItemsIndexedFromLoadedGraph: false,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.ProjectReferences,
                "Project.ProjectReferences among loaded instances.",
                "Change of a referenced project file that is actually loaded.",
                withProjectReferences,
                "A reference that evaluated only as metadata is not a loaded project instance.",
                ItemsIndexedFromLoadedGraph: true,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.MetadataDependencies,
                "PortableExecutableReference paths on loaded projects.",
                "Change of the referenced assembly file.",
                withMetadata,
                metadataOnly.Count == 0
                    ? "Shared framework and package references are recorded and are not loaded projects."
                    : "A metadata reference that is not a loaded project's output and not a shared framework or package reference leaves whole-graph coverage unknown.",
                ItemsIndexedFromLoadedGraph: true,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.ExternalGlobs,
                "Known files whose path is outside every loaded project directory. The glob pattern itself is not on the public workspace.",
                "Change of those known files. A new sibling is an unknown appearance, not a recursive region.",
                owners,
                "No recursive watcher is placed on the foreign directory. Completeness of further nested inputs is unknown.",
                ItemsIndexedFromLoadedGraph: false,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.CustomTasks,
                "Not available on the public Roslyn workspace.",
                "None indexed.",
                owners,
                "Custom task inputs stay unknown.",
                ItemsIndexedFromLoadedGraph: false,
                CompletenessKnown: false),
            new InputCategoryEvidence(
                InputCategoryKind.MultiTargeting,
                "Count of loaded project objects for a .csproj compared with TargetFramework / TargetFrameworks text in that file and ancestor Directory.Build.props. Conditions are not evaluated.",
                "A change of the project file or of those props can change which TFM is loaded.",
                owners,
                multiTarget == InputCoverage.Complete
                    ? "Exactly one declared TFM and one loaded instance for every project file. Other TFM inputs are not claimed."
                    : "Another TFM, a missing declaration, or more than one loaded object for the same file makes coverage of the whole graph unknown. A second instance is not invented.",
                ItemsIndexedFromLoadedGraph: true,
                CompletenessKnown: multiTarget == InputCoverage.Complete),
        ];
    }

    private static InputCategoryEvidence Category(
        InputCategoryKind kind,
        IReadOnlyList<ProjectId> owners,
        string dataSource,
        string watchedEvents,
        string limits)
    {
        return new InputCategoryEvidence(
            kind,
            dataSource,
            watchedEvents,
            owners,
            limits,
            ItemsIndexedFromLoadedGraph: true,
            CompletenessKnown: false);
    }

    private static ProjectId[] OwnersOfExtension(IReadOnlyList<InputPathEntry> paths, string extension)
    {
        return paths
            .Where(entry => entry.CanonicalPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static entry => entry.Occurrences.Select(static occurrence => occurrence.ProjectId))
            .Distinct()
            .ToArray();
    }

    private static ProjectId[] OwnersOfResource(IReadOnlyList<InputPathEntry> paths)
    {
        return paths
            .Where(static entry => IsResourceExtension(entry.CanonicalPath))
            .SelectMany(static entry => entry.Occurrences.Select(static occurrence => occurrence.ProjectId))
            .Distinct()
            .ToArray();
    }

    private static bool IsResourceExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".resx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".resources", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSolutionFile(string canonicalPath)
    {
        var extension = Path.GetExtension(canonicalPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedFrameworkOrPackage(string canonicalPath)
    {
        var normalized = canonicalPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return normalized.Contains($"{separator}.nuget{separator}packages{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}packs{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Microsoft.NETCore.App{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Microsoft.AspNetCore.App{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Microsoft.WindowsDesktop.App{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ProjectDirectories(IReadOnlyList<LoadedProjectInstance> instances)
    {
        var directories = new List<string>();
        foreach (var instance in instances)
        {
            if (instance.FilePath is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(instance.FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                directories.Add(directory);
            }
        }

        return directories.ToArray();
    }

    private static void Remember(HashSet<string> paths, string? rawPath)
    {
        var canonical = InputPathCanon.TryCanonicalize(rawPath);
        if (canonical is not null)
        {
            paths.Add(canonical);
        }
    }

    private static void Add(
        Dictionary<string, List<InputOccurrence>> occurrences,
        string? rawPath,
        InputOccurrence occurrence)
    {
        var canonical = InputPathCanon.TryCanonicalize(rawPath);
        if (canonical is null)
        {
            return;
        }

        if (!occurrences.TryGetValue(canonical, out var list))
        {
            list = new List<InputOccurrence>();
            occurrences.Add(canonical, list);
        }

        list.Add(occurrence);
    }

    private sealed class WatcherSeed
    {
        public string Directory { get; }

        public bool IncludeSubdirectories { get; }

        public HashSet<string> PointFilters { get; } = new(InputPathCanon.Comparer);

        public HashSet<InputRole> Roles { get; } = new();

        public WatcherSeed(string directory, bool includeSubdirectories, string evidence)
        {
            Directory = directory;
            IncludeSubdirectories = includeSubdirectories;
            _evidence = evidence;
        }

        public InputWatcherDescriptor ToDescriptor()
        {
            var filters = PointFilters.ToArray();
            Array.Sort(filters, InputPathCanon.Comparer);
            var roles = Roles.ToArray();
            return new InputWatcherDescriptor(Directory, IncludeSubdirectories, filters, roles, _evidence);
        }

        private readonly string _evidence;
    }

    private static readonly string[] WalkUpFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    ];
}
