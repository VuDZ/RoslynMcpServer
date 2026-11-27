namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Whether the next launch may skip a build. <see cref="SkipAllowed"/> is true only
/// for <see cref="BuildFreshnessState.Current"/> with an empty blocker list.
/// </summary>
internal sealed class BuildReuseDecision
{
    public BuildFreshnessState State { get; }

    public bool SkipAllowed { get; }

    public IReadOnlyList<string> Blockers { get; }

    public BuildReuseDecision(BuildFreshnessState state, bool skipAllowed, IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        State = state;
        SkipAllowed = skipAllowed && state == BuildFreshnessState.Current;
        Blockers = blockers;
    }
}
