namespace RoslynMcpServer.Diagnostics;

internal readonly record struct ReportedTestCounts(int? Passed, int? Failed, int? Skipped)
{
    internal ReportedTestCounts Add(ReportedTestCounts other) =>
        new(Sum(Passed, other.Passed), Sum(Failed, other.Failed), Sum(Skipped, other.Skipped));

    internal ReportedTestCounts AtLeast(ReportedTestCounts other) =>
        new(Max(Passed, other.Passed), Max(Failed, other.Failed), Max(Skipped, other.Skipped));

    internal string FormatLowerBounds()
    {
        var fields = new List<string>(3);
        if (Passed is { } passed)
        {
            fields.Add($"{passed} passed");
        }

        if (Failed is { } failed)
        {
            fields.Add($"{failed} failed");
        }

        if (Skipped is { } skipped)
        {
            fields.Add($"{skipped} skipped");
        }

        return fields.Count == 0
            ? "Total tests: unknown."
            : $"Total tests: unknown. Confirmed lower bounds: {string.Join(", ", fields)}.";
    }

    private static int? Sum(int? left, int? right) => right is { } value ? left.GetValueOrDefault() + value : left;

    private static int? Max(int? left, int? right) => right is { } value ? Math.Max(left.GetValueOrDefault(), value) : left;
}
