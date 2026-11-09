namespace AffectedTestsProbe;

/// <summary>Initial reverse-search limits. A change belongs in the measurement report.</summary>
internal sealed record ProbeLimits(int Depth, int Members, TimeSpan PerMember)
{
    public static ProbeLimits Default { get; } = new(30, 20_000, TimeSpan.FromSeconds(60));
}
