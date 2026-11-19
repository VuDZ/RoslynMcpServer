namespace AffectedTestsProbe;

internal sealed class Reachability
{
    public Dictionary<MemberNode, HashSet<TestUnit>> Cha { get; } = new();

    public Dictionary<MemberNode, HashSet<TestUnit>> Rta { get; } = new();

    public Dictionary<MemberNode, int> ShortestCha { get; } = new();

    public Dictionary<MemberNode, int> ShortestRta { get; } = new();

    public List<string> Limitations { get; } = new();

    public IReadOnlyCollection<TestUnit> Units(DispatchKind kind, MemberNode node)
    {
        var map = kind == DispatchKind.Cha ? Cha : Rta;
        return map.TryGetValue(node, out var units) ? units : [];
    }
}
