namespace BuildHostFork.Trace;

/// <summary>
/// A project in one effective context: a design-time build for a target framework, or a project known only from the
/// solution or a reference, which has no target framework here. Configuration and platform are the evaluated values.
/// </summary>
internal sealed record GraphNode(
    string Path,
    string? TargetFramework,
    string? Configuration,
    string? Platform,
    bool Built,
    string? SolutionEntryKind);
