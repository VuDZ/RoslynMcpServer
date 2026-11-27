namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>A dependency graph with the answers for its test classes and the assemblies that are missing from it.</summary>
/// <param name="Unread">
/// Assemblies that were not on disk or could not be read. The graph is built from the rest, so an answer
/// that involves one of these assemblies is incomplete.
/// </param>
internal sealed record IlGraphBuildResult(
    IlDependencyGraph Graph,
    IlTestClosures Closures,
    IReadOnlyList<IlUnreadAssembly> Unread);
