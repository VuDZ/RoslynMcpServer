using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>
/// The test classes of one test project by stage. A node of the graph is a top-level type; the classes of the
/// current inventory with that top-level type, nested ones included, stand for it.
/// </summary>
internal sealed class TestClassStageBuilder
{
    private TestClassStageBuilder(TestNarrowingInput input, Project project, TypeChangeSet changes)
    {
        _input = input;
        _project = project;
        _changes = changes;
        _assembly = project.AssemblyName;
        _classes = [.. input.Classes.Where(testClass => testClass.ProjectId == project.Id)];
        foreach (var testClass in _classes.Where(testClass => testClass.TopLevelTypeName is not null))
        {
            if (!_classesByTopLevel.TryGetValue(testClass.TopLevelTypeName!, out var classes))
            {
                classes = [];
                _classesByTopLevel.Add(testClass.TopLevelTypeName!, classes);
            }

            classes.Add(testClass);
        }
    }

    /// <summary>Four stages in the order of <see cref="TestClassStageLevel"/>, each holding the previous one.</summary>
    internal static IReadOnlyList<TestClassStage> Build(
        TestNarrowingInput input,
        Project project,
        TypeChangeSet changes,
        CancellationToken cancellationToken) =>
        new TestClassStageBuilder(input, project, changes).Build(cancellationToken);

    private IReadOnlyList<TestClassStage> Build(CancellationToken cancellationToken)
    {
        var stages = new List<TestClassStage>();
        AddChanged();
        stages.Add(Stage(TestClassStageLevel.Changed, cancellationToken));

        // A class the graph has no test class for may depend on anything; without a change there is nothing to
        // depend on, and the answer stays "code unchanged".
        if (!_changes.IsEmpty)
        {
            foreach (var testClass in _classes.Where(testClass => !IsGraphTestClass(testClass)))
            {
                Select(testClass, TestClassSelectionReasons.NoGraphNode);
            }
        }

        foreach (var (level, stage) in _reachingStages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddReaching(stage);
            stages.Add(Stage(level, cancellationToken));
        }

        return stages;
    }

    /// <summary>Test classes whose own fingerprint changed, and the new and changed tests of epoch 1.</summary>
    private void AddChanged()
    {
        foreach (var type in _changes.InCurrentGraph.Where(IsOfProject))
        {
            var classes = ClassesOf(type);
            foreach (var testClass in classes)
            {
                Select(testClass, TestClassSelectionReasons.ChangedFingerprint);
            }

            if (classes.Count == 0 && IsCurrentTestClass(type))
            {
                Block(UnmappedTestClass(type));
            }
        }

        foreach (var test in _input.Impact.Tests.Where(test => test.ProjectId == _project.Id))
        {
            var classes = _classes.Where(testClass => testClass.Tests.Contains(test.Key)).ToArray();
            foreach (var testClass in classes)
            {
                Select(testClass, TestClassSelectionReasons.ChangedTest);
            }

            if (classes.Length == 0)
            {
                Block(new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.UnresolvedTestClass,
                    test.Key.ContainingTypePath + "." + test.Key.Name,
                    "Unknown runtime identity for selected test: " + test.Key.ContainingTypePath + "." + test.Key.Name));
            }
        }
    }

    /// <summary>
    /// Changed types are looked for in both graphs, added ones in the current graph and removed ones in the
    /// graph before the edit. A test class found only before the edit that is no test class of the current graph
    /// was deleted with the edit; a test class of the current graph that the inventory lacks, whichever graph
    /// found it, cannot be named in a filter.
    /// </summary>
    private void AddReaching(IlDependencyStage stage)
    {
        foreach (var type in _input.Pair.CurrentGraph.Closures.AffectedTestClasses(_changes.InCurrentGraph, stage).Where(IsOfProject))
        {
            var classes = ClassesOf(type);
            foreach (var testClass in classes)
            {
                Select(testClass, TestClassSelectionReasons.ReachesChangedType);
            }

            if (classes.Count == 0)
            {
                Block(UnmappedTestClass(type));
            }
        }

        foreach (var type in _input.Pair.BeforeGraph.Closures.AffectedTestClasses(_changes.InBeforeGraph, stage).Where(IsOfProject))
        {
            var classes = ClassesOf(type);
            foreach (var testClass in classes)
            {
                Select(testClass, TestClassSelectionReasons.ReachesChangedType);
            }

            // Only a class the edit deleted may be skipped; one that is still a test class lost only its route.
            if (classes.Count == 0 && IsCurrentTestClass(type))
            {
                Block(UnmappedTestClass(type));
            }
        }
    }

    private TestClassStage Stage(TestClassStageLevel level, CancellationToken cancellationToken)
    {
        TestClassSelection[] selections =
        [
            .. _classes.Where(_reasons.ContainsKey).Select(testClass => new TestClassSelection(testClass, _reasons[testClass])),
        ];
        if (_blocker is not null)
        {
            return new TestClassStage(level, selections, null, _blocker);
        }

        var filter = VstestRunPlanner.PlanClasses([.. selections.Select(selection => selection.Class)], cancellationToken);
        return filter switch
        {
            VstestClassFilterExpression(var expression) => new TestClassStage(level, selections, expression, null),
            VstestClassFilterEmpty => new TestClassStage(level, selections, null, null),
            VstestClassFilterWholeProject(var reason) => new TestClassStage(level, selections, null, reason),
        };
    }

    private void Select(VstestTestClass testClass, TestClassSelectionReasons reason) =>
        _reasons[testClass] = _reasons.GetValueOrDefault(testClass) | reason;

    /// <summary>The first reason a stage cannot be a class filter holds for every wider stage too.</summary>
    private void Block(TestNarrowingWholeReason reason) => _blocker ??= reason;

    private bool IsOfProject(IlTypeName type) => string.Equals(type.Assembly, _assembly, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<VstestTestClass> ClassesOf(IlTypeName type) =>
        IsOfProject(type) && _classesByTopLevel.TryGetValue(type.Name, out var classes) ? classes : [];

    private bool IsGraphTestClass(VstestTestClass testClass) =>
        testClass.TopLevelTypeName is not null && IsCurrentTestClass(new IlTypeName(_assembly, testClass.TopLevelTypeName));

    /// <summary>A generic or abstract top-level type has a node but is no test class of the graph.</summary>
    private bool IsCurrentTestClass(IlTypeName type)
    {
        var graph = _input.Pair.CurrentGraph.Graph;
        return graph.NodeByType.TryGetValue(type, out var node) && graph.TestMethods[node] > 0;
    }

    private static TestNarrowingWholeReason UnmappedTestClass(IlTypeName type) => new(
        TestNarrowingWholeKind.UnresolvedTestClass,
        type.ToString(),
        $"Test class {type} of the compiled assembly has no class in the source inventory to name in a filter.");

    private static readonly (TestClassStageLevel Level, IlDependencyStage Stage)[] _reachingStages =
    [
        (TestClassStageLevel.References, IlDependencyStage.References),
        (TestClassStageLevel.Discovery, IlDependencyStage.Discovery),
        (TestClassStageLevel.DiscoveryWithLoads, IlDependencyStage.DiscoveryWithLoads),
    ];

    private readonly TestNarrowingInput _input;
    private readonly Project _project;
    private readonly TypeChangeSet _changes;
    private readonly string _assembly;
    private readonly VstestTestClass[] _classes;
    private readonly Dictionary<string, List<VstestTestClass>> _classesByTopLevel = new(StringComparer.Ordinal);
    private readonly Dictionary<VstestTestClass, TestClassSelectionReasons> _reasons = new(ReferenceEqualityComparer.Instance);
    private TestNarrowingWholeReason? _blocker;
}
