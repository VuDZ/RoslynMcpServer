namespace AffectedTestsProbe;

/// <summary>
/// Case-level pair check, before rows collapse into a unit.
/// A unit with one failing mutation row stays in the oracle even when its other rows disappear.
/// Two control rows and one passing mutation row are missing results.
/// </summary>
internal static class PairValidity
{
    public static PairJudgment Judge(IReadOnlyList<ExecutedCase> control, IReadOnlyList<ExecutedCase> mutation)
    {
        var controlByUnit = control.GroupBy(row => row.UnitKey).ToDictionary(group => group.Key, group => group.ToArray());
        var mutationByUnit = mutation.GroupBy(row => row.UnitKey).ToDictionary(group => group.Key, group => group.ToArray());
        var failed = new List<string>();
        foreach (var unit in controlByUnit.Keys.Union(mutationByUnit.Keys))
        {
            controlByUnit.TryGetValue(unit, out var left);
            mutationByUnit.TryGetValue(unit, out var right);
            left ??= [];
            right ??= [];
            if (right.Any(row => row.Outcome == "Failed"))
            {
                if (HasExtra(left, right))
                {
                    return new PairJudgment(PairStatus.MissingResults, []);
                }

                failed.Add(unit);
                continue;
            }

            if (!SameNames(left, right))
            {
                return new PairJudgment(PairStatus.MissingResults, []);
            }

            if (!SameNames(left.Where(IsSkipped).ToArray(), right.Where(IsSkipped).ToArray()))
            {
                return new PairJudgment(PairStatus.SkippedMismatch, []);
            }
        }

        return new PairJudgment(PairStatus.Valid, failed);
    }

    private static bool HasExtra(ExecutedCase[] control, ExecutedCase[] mutation)
    {
        var counts = control.GroupBy(row => row.FullyQualifiedName).ToDictionary(group => group.Key, group => group.Count());
        foreach (var group in mutation.GroupBy(row => row.FullyQualifiedName))
        {
            if (!counts.TryGetValue(group.Key, out var count) || group.Count() > count)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameNames(ExecutedCase[] left, ExecutedCase[] right) =>
        Names(left).SequenceEqual(Names(right));

    private static IEnumerable<string> Names(ExecutedCase[] rows) =>
        rows.Select(row => row.FullyQualifiedName).OrderBy(name => name, StringComparer.Ordinal);

    private static bool IsSkipped(ExecutedCase row) => row.Outcome is "Skipped" or "NotExecuted";
}
