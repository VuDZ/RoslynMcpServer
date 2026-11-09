namespace AffectedTestsProbe;

/// <summary>
/// Reverse search result. When <see cref="Limit"/> is set the partial unit set is not an answer:
/// the caller substitutes level 0 and drops the member from the CHA agreement.
/// </summary>
internal sealed class ReverseOutcome
{
    public HashSet<TestUnit> Units { get; } = new();

    public string? Limit { get; set; }

    public int Visited { get; set; }

    public int Depth { get; set; }

    public long ElapsedMilliseconds { get; set; }
}
