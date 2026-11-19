namespace TestImpactIlProbe;

/// <summary>Percentiles of a set of shares, each between 0 and 1.</summary>
internal sealed record Distribution(
    int Count,
    double P10,
    double P25,
    double Median,
    double P75,
    double P90,
    double P99,
    double Mean,
    int Zero,
    int AtLeast80Percent)
{
    public static Distribution Of(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return new Distribution(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        double At(double fraction) => Math.Round(sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(fraction * sorted.Length))], 4);
        return new Distribution(
            sorted.Length,
            At(0.10),
            At(0.25),
            At(0.50),
            At(0.75),
            At(0.90),
            At(0.99),
            Math.Round(sorted.Average(), 4),
            sorted.Count(value => value == 0),
            sorted.Count(value => value >= 0.8));
    }
}
