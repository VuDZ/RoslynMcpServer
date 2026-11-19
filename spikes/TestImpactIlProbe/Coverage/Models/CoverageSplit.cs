namespace TestImpactIlProbe;

/// <summary>A set of files divided by what the coverage reports say about them.</summary>
internal sealed record CoverageSplit(int Files, int Covered, int NotCovered, int NotInReport)
{
    public static CoverageSplit Of(IEnumerable<FileCoverage> files)
    {
        var counts = new int[3];
        foreach (var file in files)
        {
            counts[(int)file]++;
        }

        return new CoverageSplit(
            counts.Sum(),
            counts[(int)FileCoverage.Covered],
            counts[(int)FileCoverage.NotCovered],
            counts[(int)FileCoverage.NotInReport]);
    }
}
