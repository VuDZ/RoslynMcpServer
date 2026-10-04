namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Finite oracle fixture scenarios of implementation packet E0/task-03: the fixed disk layouts of
/// the epoch-0 admission/equivalence experiments. The set is closed; a new scenario requires a
/// packet update, not a fixture-side decision.
/// </summary>
internal enum FixtureScenario
{
    ProjectGraph,
    Encodings,
    MultiTarget,
    RazorGenerator,
    GeneratedObj,
    LinkedImport,
    AbsentImport,
    CustomTarget,
    ExplicitCompile,
    WebAssets,
    ExternalAsset,
    MissingReference,
    EmptyProject
}
