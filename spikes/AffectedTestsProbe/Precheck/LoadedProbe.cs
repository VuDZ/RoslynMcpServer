using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

internal sealed class LoadedProbe : IAsyncDisposable
{
    private readonly ProbeSession _session;

    public LoadedProbe(ProbeSession session, ProjectMap map, FactGraph facts, RootIndex roots, Reachability reach)
    {
        _session = session;
        Map = map;
        Facts = facts;
        Roots = roots;
        Reach = reach;
    }

    public Solution Solution => _session.Solution;

    public ProjectMap Map { get; }

    public FactGraph Facts { get; }

    public RootIndex Roots { get; }

    public Reachability Reach { get; }

    public static async Task<LoadedProbe> LoadAsync(ProbeSession session, CancellationToken cancellationToken)
    {
        var map = new ProjectMap(session.Solution, "unspecified", "unspecified");
        var facts = await FactExtractor.ExtractAsync(session.Solution, map, cancellationToken).ConfigureAwait(false);
        var roots = await RootIndexBuilder.BuildAsync(session.Solution, map, [], cancellationToken).ConfigureAwait(false);
        var reach = ForwardReachability.Compute(facts, roots, map);
        return new LoadedProbe(session, map, facts, roots, reach);
    }

    public Task<ReverseOutcome> ReverseAsync(MemberNode node, CancellationToken cancellationToken) =>
        ReverseSearch.SearchAsync(Solution, Map, Roots, node, ProbeLimits.Default, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _session.Dispose();
        return ValueTask.CompletedTask;
    }
}
