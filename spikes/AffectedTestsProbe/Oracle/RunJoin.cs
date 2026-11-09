namespace AffectedTestsProbe;

internal sealed record JoinResult(IReadOnlyList<ExecutedCase> Cases, int Executed, int UnmappedExecuted);

/// <summary>
/// Joins one run's discovery id to its TRX row, then the fully qualified name to a source unit.
/// The display name is ignored even when it looks like a unit.
/// </summary>
internal static class RunJoin
{
    public static JoinResult Join(
        IReadOnlyList<DiscoveredCase> discovered,
        IReadOnlyList<TrxRow> rows,
        IReadOnlyList<TestUnit> units)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in discovered.GroupBy(item => item.Id))
        {
            var items = group.Take(2).ToArray();
            if (items.Length == 1)
            {
                names.Add(items[0].Id, items[0].FullyQualifiedName);
            }
        }

        var cases = new List<ExecutedCase>();
        var executed = 0;
        var unmapped = 0;
        foreach (var row in rows)
        {
            var ran = row.Outcome is "Passed" or "Failed";
            if (ran)
            {
                executed++;
            }

            if (!names.TryGetValue(row.TestId, out var fullyQualifiedName))
            {
                if (ran)
                {
                    unmapped++;
                }

                continue;
            }

            var unit = FqnMapper.Map(fullyQualifiedName, units);
            if (unit is null)
            {
                if (ran)
                {
                    unmapped++;
                }

                continue;
            }

            cases.Add(new ExecutedCase(unit.Key, fullyQualifiedName, row.Outcome));
        }

        return new JoinResult(cases, executed, unmapped);
    }
}
