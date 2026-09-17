namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Per-failure Standard Output/Error budgets for VSTest markdown reports.
/// Parse keeps a larger cap; the report applies these at render time.
/// </summary>
public readonly record struct TestOutputReportOptions(bool IncludeFullOutput, int MaxOutputChars)
{
    public static TestOutputReportOptions Default => new(false, 0);

    public const int DefaultStdOutChars = 2500;
    public const int DefaultStdOutHeadChars = 1600;
    public const int DefaultStdOutTailChars = 700;
    public const int DefaultStdErrChars = 1000;
    public const int DefaultStdErrHeadChars = 400;
    public const int DefaultStdErrTailChars = 500;
    public const int FullOutputSafetyCapChars = 100_000;

    /// <summary>Parse-time cap so a multi-megabyte console dump is not held in full.</summary>
    internal const int ParseStreamCapChars = 200_000;

    public int StdOutBudget
    {
        get
        {
            var requested = MaxOutputChars > 0
                ? MaxOutputChars
                : IncludeFullOutput
                    ? FullOutputSafetyCapChars
                    : DefaultStdOutChars;
            return Math.Clamp(requested, 0, FullOutputSafetyCapChars);
        }
    }

    public int StdErrBudget
    {
        get
        {
            if (IncludeFullOutput && MaxOutputChars <= 0)
            {
                return FullOutputSafetyCapChars;
            }

            var stdout = StdOutBudget;
            if (stdout <= 0)
            {
                return 0;
            }

            var scaled = (int)Math.Round(stdout * (double)DefaultStdErrChars / DefaultStdOutChars);
            return Math.Clamp(scaled, Math.Min(400, stdout), stdout);
        }
    }

    public string FormatMetadata()
    {
        var extra = IncludeFullOutput ? " (`includeFullOutput`)" : string.Empty;
        if (MaxOutputChars > 0)
        {
            extra += $" (`maxOutputChars`={MaxOutputChars})";
        }

        return $"- **TestOutput:** StdOut {StdOutBudget} chars, StdErr {StdErrBudget} chars{extra}";
    }

    internal static (int Head, int Tail) ScaleHeadTail(int budget, int defaultBudget, int defaultHead, int defaultTail)
    {
        if (budget <= 0)
        {
            return (0, 0);
        }

        if (budget == defaultBudget)
        {
            return (defaultHead, defaultTail);
        }

        var head = Math.Max(1, (int)Math.Round(budget * (double)defaultHead / defaultBudget));
        var tail = Math.Max(1, (int)Math.Round(budget * (double)defaultTail / defaultBudget));
        if (head + tail > budget)
        {
            head = Math.Max(1, Math.Min(head, budget - 1));
            tail = Math.Max(1, budget - head);
        }

        return (head, tail);
    }
}
