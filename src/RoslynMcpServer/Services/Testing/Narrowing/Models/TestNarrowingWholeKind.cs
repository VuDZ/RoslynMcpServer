namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>Why a test project selected whole by epoch 1 is not narrowed to test classes.</summary>
internal enum TestNarrowingWholeKind
{
    /// <summary>Epoch 1 selected the project for a changed non-C# input.</summary>
    NonCSharpInput,

    /// <summary>Epoch 1 selected the project for a changed project, solution or MSBuild file.</summary>
    ProjectFile,

    /// <summary>The project graph is stale: the projects on the path to the test project may be wrong.</summary>
    ProjectGraphStale,

    /// <summary>The non-C# input history of the baseline is incomplete.</summary>
    ObservationGap,

    /// <summary>
    /// The assembly snapshots belong to another baseline than the epoch 1 estimate: a different solution was
    /// loaded between the two. Repeating the request computes both against one baseline.
    /// </summary>
    BaselineChanged,

    /// <summary>An assembly on the path has no snapshot before the edit: it was not on disk, or its project appeared later.</summary>
    NoBeforeSnapshot,

    /// <summary>An assembly on the path may have been rebuilt before its first read, so the state before the edit is unknown.</summary>
    BeforeSnapshotUnknown,

    /// <summary>An assembly on the path could not be read.</summary>
    AssemblyUnreadable,

    /// <summary>An assembly on the path was read, but some method bodies were not: their edges are missing from the graph.</summary>
    UnreadableMethodBodies,

    /// <summary>The output assembly of a project with edited C# documents is not newer than the last edit, or does not exist.</summary>
    NotRebuilt,

    /// <summary>An edited C# document no longer exists, so the time of the last edit is unknown.</summary>
    EditedDocumentMissing,

    /// <summary>
    /// An assembly on the path is older than the file MSBuild compares to decide whether to recompile it: the
    /// reference assembly of a dependency on the path, or its output assembly when it makes no reference assembly.
    /// </summary>
    DependentNotRebuilt,

    /// <summary>A project on the path declares a reference assembly, and the file does not exist.</summary>
    ReferenceAssemblyMissing,

    /// <summary>A selected test class has no runtime name for a VSTest filter.</summary>
    UnresolvedTestClass,

    /// <summary>A selected test class is a fixture with constructor or source arguments.</summary>
    FixtureArguments,

    /// <summary>The class filter is longer than the budget of the run planner.</summary>
    FilterOverBudget,

    /// <summary>Narrowing threw after a successful build; the project runs whole rather than not at all.</summary>
    NarrowingFailed,
}
