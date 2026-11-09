namespace AffectedTestsProbe;

internal sealed class FrameBResult
{
    public FrameBResult(
        IReadOnlyList<SampleMember> ordinary,
        IReadOnlyList<SampleMember> virtualMembers,
        IReadOnlyList<SampleMember> di,
        IReadOnlyList<SampleMember> stat,
        IReadOnlyList<SampleMember> selected,
        string hash)
    {
        Ordinary = ordinary;
        Virtual = virtualMembers;
        Di = di;
        Static = stat;
        Selected = selected;
        Hash = hash;
    }

    public IReadOnlyList<SampleMember> Ordinary { get; }

    public IReadOnlyList<SampleMember> Virtual { get; }

    public IReadOnlyList<SampleMember> Di { get; }

    public IReadOnlyList<SampleMember> Static { get; }

    public IReadOnlyList<SampleMember> Selected { get; }

    public string Hash { get; }

    public bool DiEmpty => Di.Count == 0;
}
