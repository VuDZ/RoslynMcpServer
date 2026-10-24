namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// BuildHost processes to terminate after a cancelled load, and how many descendant processes were left
/// alone because their command line could not be read.
/// </summary>
internal readonly record struct UnregisteredBuildHostReapPlan(
    IReadOnlyList<int> ProcessIds,
    int UnidentifiedDescendants);
