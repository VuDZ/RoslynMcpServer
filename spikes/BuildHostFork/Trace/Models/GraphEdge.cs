namespace BuildHostFork.Trace;

/// <summary>
/// A dependency observed during the load and where it came from: a ProjectReference item of the evaluated and built
/// project, a load of the referenced project the client started while resolving references, or an output path query.
/// The target is a path: which of its target frameworks the reference uses is not observed here. ReferenceOutputAssembly
/// is known only for items; null means not observed.
/// </summary>
internal sealed record GraphEdge(
    string From,
    string? FromTargetFramework,
    string To,
    string Provenance,
    bool? ReferenceOutputAssembly);
