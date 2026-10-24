namespace BuildHostFork.Trace;

/// <summary>
/// A project in one effective context: a design-time build for a target framework, or a project known only from the
/// solution or a reference, which has no target framework here. Configuration and platform are the evaluated values.
/// </summary>
/// <remarks>
/// The key is the path and the target framework. Configuration and platform are labels from the path's first root
/// evaluation, and other global properties are not recorded: the 5.9.0 loader gives one set of global properties to
/// the whole load and the host varies only TargetFramework, so two contexts of one path that differ otherwise would
/// merge into one node.
/// </remarks>
internal sealed record GraphNode(
    string Path,
    string? TargetFramework,
    string? Configuration,
    string? Platform,
    bool Built,
    string? SolutionEntryKind);
