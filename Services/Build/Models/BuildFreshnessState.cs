namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Freshness of one project in one build context for the current process.
/// <see cref="Current"/> is reachable only when a proof's cursor matches the pull
/// and that cursor carries input content hashes. The shared snapshot does not
/// carry those hashes, so a successful build stays <see cref="Unknown"/> or <see cref="Dirty"/>.
/// </summary>
internal enum BuildFreshnessState
{
    Unknown = 0,
    Dirty,
    Building,
    Current,
}
