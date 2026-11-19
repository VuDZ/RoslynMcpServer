namespace TestImpactIlProbe;

/// <summary>
/// Files of one layer divided by whether the layer selects any test for them, and then by coverage. A file
/// with an empty answer that a test executes is a dependency the graph does not see.
/// </summary>
internal sealed record AnswerCoverage(CoverageSplit EmptyAnswer, CoverageSplit NonEmptyAnswer)
{
    /// <summary>Among the files some test executes, the share the layer selects no test for.</summary>
    public double MissedShareOfCovered => Share(EmptyAnswer.Covered, EmptyAnswer.Covered + NonEmptyAnswer.Covered);

    /// <summary>Among the empty answers the reports can judge, the share some test executes.</summary>
    public double CoveredShareOfEmpty => Share(EmptyAnswer.Covered, EmptyAnswer.Covered + EmptyAnswer.NotCovered);

    private static double Share(int part, int whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);
}
