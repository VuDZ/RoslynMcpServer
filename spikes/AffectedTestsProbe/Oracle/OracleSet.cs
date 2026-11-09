namespace AffectedTestsProbe;

/// <summary>
/// Oracle O is a unit that failed after the mutation and passed in the control run.
/// Units that already failed in the control run are listed apart and are not O.
/// </summary>
internal static class OracleSet
{
    public static string[] Affected(IReadOnlyList<string> mutationFailed, IReadOnlyList<ExecutedCase> control)
    {
        var alreadyFailed = control
            .Where(row => row.Outcome == "Failed")
            .Select(row => row.UnitKey)
            .ToHashSet(StringComparer.Ordinal);
        return mutationFailed
            .Where(unit => !alreadyFailed.Contains(unit))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(unit => unit, StringComparer.Ordinal)
            .ToArray();
    }
}
