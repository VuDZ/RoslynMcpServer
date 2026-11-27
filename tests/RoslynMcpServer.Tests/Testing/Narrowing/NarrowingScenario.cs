using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Testing;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using RoslynMcpServer.Tests.SourceStructure;
using RoslynMcpServer.Tests.Testing.Il;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>
/// A solution in two versions, before and after the edit, compiled in the test. Both snapshots are built from
/// the compiled images without any file, and the test sets every time: of the output and reference assemblies
/// and of the edited documents. By default an assembly whose code differs between the versions was rebuilt
/// after the edit (<see cref="Built"/>), and every other file dates from before publication (<see cref="Loaded"/>).
/// The epoch 1 answer is made from the edits: an edit of a project selects whole every test project that
/// depends on it, as epoch 1 does for a C# edit it cannot narrow.
/// </summary>
internal sealed class NarrowingScenario : IDisposable
{
    internal DateTime Published { get; } = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    internal DateTime Loaded => Published.AddHours(-1);

    internal DateTime Edited => Published.AddMinutes(10);

    internal DateTime Built => Published.AddMinutes(20);

    internal Solution Solution => _solution ?? throw new InvalidOperationException("Run the scenario first.");

    internal IReadOnlyList<VstestTestClass> Classes { get; private set; } = [];

    /// <summary>No file is written there: the paths only name the projects and their assemblies.</summary>
    private static string Root { get; } = Path.Combine(Path.GetTempPath(), "RoslynMcpNarrowingScenario");

    /// <summary>The runtime of the test process and xUnit, whose attributes the test classes carry.</summary>
    private static IEnumerable<MetadataReference> FrameworkReferences
    {
        get
        {
            var runtime = MetadataReferenceSets.Runtime();
            var xunit = typeof(FactAttribute).Assembly.Location;
            var hasXunit = runtime.Any(reference =>
                string.Equals(Path.GetFileName(reference.Display), Path.GetFileName(xunit), StringComparison.OrdinalIgnoreCase));
            return hasXunit ? runtime : runtime.Append(MetadataReference.CreateFromFile(xunit));
        }
    }

    public void Dispose() => _workspace.Dispose();

    internal NarrowingScenario Library(string name, string before, string after, params string[] references) =>
        Add(name, before, after, isTest: false, references);

    internal NarrowingScenario TestProject(string name, string before, string after, params string[] references) =>
        Add(name, before, after, isTest: true, references);

    /// <summary>The project makes no reference assembly: its output assembly is what MSBuild compares.</summary>
    internal NarrowingScenario WithoutReferenceAssembly(string project)
    {
        _projects[project].ReferenceAssembly = false;
        return this;
    }

    internal NarrowingScenario Resources(string project, byte[] before, byte[] after)
    {
        _projects[project].BeforeResource = before;
        _projects[project].AfterResource = after;
        return this;
    }

    /// <summary>An edit of a C# document of the project at <see cref="Edited"/>.</summary>
    /// <param name="selectsWhole">
    /// False for an edit epoch 1 narrows itself, such as a changed test method: the document is edited, but
    /// the edit selects no project whole.
    /// </param>
    internal NarrowingScenario Edit(string project, bool selectsWhole = true) =>
        EditAt(project, Edited, project + ".cs", selectsWhole);

    /// <param name="time">Null for a document that no longer exists.</param>
    internal NarrowingScenario EditAt(string project, DateTime? time, string file, bool selectsWhole = true)
    {
        _edits.Add((project, DocumentPath(project, file), time, selectsWhole));
        return this;
    }

    /// <summary>The time of the current output assembly.</summary>
    internal NarrowingScenario Output(string project, DateTime time)
    {
        _projects[project].OutputTime = time;
        return this;
    }

    /// <param name="time">Null when the declared reference assembly does not exist.</param>
    internal NarrowingScenario ReferenceAssembly(string project, DateTime? time)
    {
        _projects[project].ReferenceTime = time;
        return this;
    }

    /// <summary>Replaces the entry before the edit; a null entry means the snapshot has none for the path.</summary>
    internal NarrowingScenario Before(string project, Func<string, AssemblySnapshotEntry?> entry)
    {
        _projects[project].BeforeEntry = entry;
        return this;
    }

    internal NarrowingScenario Current(string project, Func<string, AssemblySnapshotEntry> entry)
    {
        _projects[project].CurrentEntry = entry;
        return this;
    }

    /// <summary>The read of the assembly reports a method body it could not read.</summary>
    internal NarrowingScenario UnreadableBodies(string project, bool before)
    {
        if (before)
        {
            _projects[project].UnreadableBodiesBefore = true;
        }
        else
        {
            _projects[project].UnreadableBodiesCurrent = true;
        }

        return this;
    }

    /// <summary>Adds an epoch 1 reason for selecting the test project whole.</summary>
    internal NarrowingScenario Expand(string testProject, ProjectExpansionKind kind, string? owner, string? path)
    {
        _expansions.Add((testProject, kind, owner, path));
        return this;
    }

    /// <summary>The build copies the assembly of a project the test project does not reference next to it.</summary>
    internal NarrowingScenario Neighbor(string testProject, string project)
    {
        _projects[testProject].ExtraNeighbors.Add(project);
        return this;
    }

    /// <summary>Epoch 1 did not select the test project, whatever the edits are.</summary>
    internal NarrowingScenario NotSelectedByEpoch1(string testProject)
    {
        _notSelected.Add(testProject);
        return this;
    }

    /// <summary>A new or changed test that epoch 1 found by syntax.</summary>
    internal NarrowingScenario ChangedTest(string project, string runtimeClass, string method)
    {
        _changedTests.Add((project, runtimeClass, method));
        return this;
    }

    internal NarrowingScenario ProjectGraphStale()
    {
        _projectGraphStale = true;
        return this;
    }

    /// <summary>Changes the current inventory after it is built from the sources.</summary>
    internal NarrowingScenario Inventory(Func<IReadOnlyList<VstestTestClass>, IEnumerable<VstestTestClass>> change)
    {
        _inventoryChanges.Add(change);
        return this;
    }

    internal ProjectId Id(string project) => _projects[project].Id;

    internal string OutputPath(string project) => Path.Combine(Root, project, "bin", project + ".dll");

    internal string ReferencePath(string project) => Path.Combine(Root, project, "obj", "ref", project + ".dll");

    internal string DocumentPath(string project, string file) => Path.Combine(Root, project, file);

    /// <summary>The answer for one test project, which must be in the result.</summary>
    internal async Task<TestProjectNarrowing> RunAsync(string testProject = "Tests")
    {
        var result = await RunAllAsync();
        var project = result.Find(Id(testProject));
        Assert.True(project is not null, "The result has no answer for " + testProject + ".");
        NarrowingAssert.StagesAreNested(project);
        return project;
    }

    internal async Task<TestNarrowingResult> RunAllAsync()
    {
        _solution = BuildSolution();
        var built = await VstestTestInventory.BuildAsync(_solution, CancellationToken.None);
        var classes = built;
        foreach (var change in _inventoryChanges)
        {
            classes = [.. change(classes)];
        }

        Classes = classes;

        // Epoch 1 names its tests by the inventory built from the sources; a changed inventory tests what
        // narrowing does with a test it cannot map.
        var input = new TestNarrowingInput(
            _solution,
            Impact(built),
            classes,
            [.. _edits.Select(edit => new EditedSourceFile(Id(edit.Project), edit.Path, edit.Time))],
            ReferenceTimes(),
            Pair());
        return TestClassNarrower.Narrow(input, CancellationToken.None);
    }

    private NarrowingScenario Add(string name, string before, string after, bool isTest, string[] references)
    {
        _projects.Add(name, new ScenarioProject(name, before, after, isTest, references));
        return this;
    }

    private Solution BuildSolution()
    {
        var solution = _workspace.CurrentSolution;
        foreach (var project in _projects.Values)
        {
            var info = ProjectInfo.Create(
                project.Id,
                VersionStamp.Create(),
                project.Name,
                project.Name,
                LanguageNames.CSharp,
                filePath: Path.Combine(Root, project.Name, project.Name + ".csproj"),
                outputFilePath: OutputPath(project.Name),
                outputRefFilePath: project.ReferenceAssembly ? ReferencePath(project.Name) : null,
                metadataReferences: FrameworkReferences.Append(Package.Reference),
                projectReferences: project.References.Select(reference => new ProjectReference(Id(reference))));
            solution = solution.AddProject(info);
            var document = DocumentId.CreateNewId(project.Id);
            solution = solution.AddDocument(
                document,
                project.Name + ".cs",
                SourceText.From(project.After),
                filePath: DocumentPath(project.Name, project.Name + ".cs"));
        }

        return solution;
    }

    private TestImpactResult Impact(IReadOnlyList<VstestTestClass> classes)
    {
        var tests = new HashSet<ProjectId>(_projects.Values.Where(project => project.IsTest).Select(project => project.Id));
        var expanded = new Dictionary<ProjectId, List<ProjectExpansionReason>>();
        void Select(ProjectId project, ProjectExpansionReason reason)
        {
            if (!expanded.TryGetValue(project, out var reasons))
            {
                reasons = [];
                expanded.Add(project, reasons);
            }

            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }

        foreach (var edit in _edits.Where(edit => edit.SelectsWhole))
        {
            var reason = new ProjectExpansionReason(ProjectExpansionKind.CSharpSource, Id(edit.Project), edit.Path);
            foreach (var project in ProjectImpactMapper.ExpandProjects(Solution, [Id(edit.Project)], tests))
            {
                Select(project, reason);
            }
        }

        foreach (var expansion in _expansions)
        {
            var owner = expansion.Owner is null ? null : Id(expansion.Owner);
            Select(Id(expansion.TestProject), new ProjectExpansionReason(expansion.Kind, owner, expansion.Path));
        }

        foreach (var project in _notSelected)
        {
            expanded.Remove(Id(project));
        }

        AffectedTest[] changed =
        [
            .. _changedTests.Select(test =>
            {
                var testClass = classes.Single(item => item.ProjectId == Id(test.Project) && item.RuntimeName == test.RuntimeClass);
                var key = testClass.Tests.Single(item => item.Name == test.Method);
                return new AffectedTest(Id(test.Project), key, ["Changed test: " + test.RuntimeClass]);
            }),
        ];
        return new TestImpactResult(
            changed,
            expanded.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ProjectExpansionReason>)pair.Value),
            [],
            _projectGraphStale,
            _projectGraphStale);
    }

    private Dictionary<string, DateTime> ReferenceTimes()
    {
        var times = new Dictionary<string, DateTime>(IlPaths.Comparer);
        foreach (var project in _projects.Values.Where(project => project.ReferenceAssembly && !project.ReferenceMissing))
        {
            times[ReferencePath(project.Name)] = project.ReferenceTime ?? Loaded;
        }

        return times;
    }

    private AssemblySnapshotPair Pair()
    {
        var names = _projects.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var before = new List<AssemblySnapshotEntry>();
        var current = new List<AssemblySnapshotEntry>();
        var beforeImages = new Dictionary<string, IlAssemblyImage>();
        var currentImages = new Dictionary<string, IlAssemblyImage>();
        foreach (var project in _projects.Values)
        {
            var path = OutputPath(project.Name);
            var beforeImage = Compile(project, project.Before, project.BeforeResource, beforeImages);
            var currentImage = Compile(project, project.After, project.AfterResource, currentImages);
            beforeImages.Add(project.Name, beforeImage);
            currentImages.Add(project.Name, currentImage);
            var rebuilt = _edits.Any(edit => edit.Project == project.Name)
                || !beforeImage.Content.SequenceEqual(currentImage.Content);
            var beforeEntry = project.BeforeEntry is null
                ? Entry(beforeImage, Loaded, names, project.UnreadableBodiesBefore)
                : project.BeforeEntry(path);
            if (beforeEntry is not null)
            {
                before.Add(beforeEntry);
            }

            current.Add(project.CurrentEntry is null
                ? Entry(currentImage, project.OutputTime ?? (rebuilt ? Built : Loaded), names, project.UnreadableBodiesCurrent)
                : project.CurrentEntry(path));
        }

        var neighbors = Neighbors();
        return new AssemblySnapshotPair(
            Path.Combine(Root, "Scenario.slnx"),
            Published,
            new AssemblySnapshot(before),
            new AssemblySnapshot(current),
            neighbors,
            IlDependencyGraphBuilder.Build(GraphInputs(before), neighbors),
            IlDependencyGraphBuilder.Build(GraphInputs(current), neighbors));
    }

    private static IlAssemblyReadResult[] GraphInputs(IEnumerable<AssemblySnapshotEntry> entries) =>
        [.. entries.Where(entry => entry.Result is not null).Select(entry => entry.Result!)];

    /// <summary>A test project's output directory holds copies of every project it depends on, as a build makes.</summary>
    private IlNeighborMap Neighbors()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(IlPaths.Comparer);
        foreach (var project in _projects.Values.Where(project => project.IsTest))
        {
            map[OutputPath(project.Name)] = [.. Dependencies(project).Concat(project.ExtraNeighbors).Distinct().Select(OutputPath)];
        }

        return new IlNeighborMap(map);
    }

    private IEnumerable<string> Dependencies(ScenarioProject project) =>
        project.References.SelectMany(reference => Dependencies(_projects[reference]).Prepend(reference)).Distinct();

    private IlAssemblyImage Compile(ScenarioProject project, string source, byte[]? resource, Dictionary<string, IlAssemblyImage> compiled)
    {
        var references = Dependencies(project).Select(name => IlFingerprintCompiler.Reference(compiled[name]));
        return IlFingerprintCompiler.Compile(
            source,
            project.Name,
            FrameworkReferences.Append(Package.Reference).Concat(references),
            resource,
            OutputPath(project.Name));
    }

    private static AssemblySnapshotEntry Entry(IlAssemblyImage image, DateTime time, IReadOnlySet<string> names, bool unreadableBodies)
    {
        var read = IlAssemblyReader.Read(image);
        if (unreadableBodies)
        {
            var facts = read.Facts!;
            read = IlAssemblyReadResult.Read(read.Path, new IlAssemblyFacts
            {
                Name = facts.Name,
                UnreadableBodies = 1,
                Types = facts.Types,
                Preamble = facts.Preamble,
                Forwarders = facts.Forwarders,
            });
        }

        return AssemblySnapshotEntry.Read(
            image.Path,
            new AssemblyFileStamp(image.Content.Length, time),
            read,
            IlAssemblyFingerprints.Compute(image, names));
    }

    private sealed class ScenarioProject
    {
        internal ProjectId Id { get; } = ProjectId.CreateNewId();
        internal string Name { get; }
        internal string Before { get; }
        internal string After { get; }
        internal bool IsTest { get; }
        internal string[] References { get; }
        internal bool ReferenceAssembly { get; set; } = true;
        internal byte[]? BeforeResource { get; set; }
        internal byte[]? AfterResource { get; set; }
        internal DateTime? OutputTime { get; set; }

        internal DateTime? ReferenceTime
        {
            get => _referenceTime;
            set
            {
                _referenceTime = value;
                ReferenceMissing = value is null;
            }
        }

        internal bool ReferenceMissing { get; private set; }
        internal Func<string, AssemblySnapshotEntry?>? BeforeEntry { get; set; }
        internal Func<string, AssemblySnapshotEntry>? CurrentEntry { get; set; }
        internal bool UnreadableBodiesBefore { get; set; }
        internal bool UnreadableBodiesCurrent { get; set; }

        /// <summary>Projects that are not referenced, yet a build copies their assembly next to this test assembly.</summary>
        internal List<string> ExtraNeighbors { get; } = [];

        internal ScenarioProject(string name, string before, string after, bool isTest, string[] references)
        {
            Name = name;
            Before = before;
            After = after;
            IsTest = isTest;
            References = references;
        }

        private DateTime? _referenceTime;
    }

    /// <summary>The package assembly with the attribute a framework finds types by; it is not part of the solution.</summary>
    private static class Package
    {
        internal static MetadataReference Reference => _reference.Value;

        private static readonly Lazy<MetadataReference> _reference = new(() =>
            IlFingerprintCompiler.Reference(IlFingerprintCompiler.Compile(NarrowingSources.Package, "Package", path: "missing/Package.dll")));
    }

    private readonly AdhocWorkspace _workspace = new();
    private readonly Dictionary<string, ScenarioProject> _projects = [];
    private readonly List<(string Project, string Path, DateTime? Time, bool SelectsWhole)> _edits = [];
    private readonly List<(string TestProject, ProjectExpansionKind Kind, string? Owner, string? Path)> _expansions = [];
    private readonly List<string> _notSelected = [];
    private readonly List<(string Project, string RuntimeClass, string Method)> _changedTests = [];
    private readonly List<Func<IReadOnlyList<VstestTestClass>, IEnumerable<VstestTestClass>>> _inventoryChanges = [];
    private Solution? _solution;
    private bool _projectGraphStale;
}
