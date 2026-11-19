namespace AffectedTestsProbe;

/// <summary>Completeness is not published when unmapped executed cases are more than 5 percent.</summary>
internal static class CompletenessGate
{
    public static bool Publish(int executed, int unmappedExecuted) =>
        executed > 0 && unmappedExecuted * 20 <= executed;
}
