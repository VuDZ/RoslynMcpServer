using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class CoverageComparisonTests
{
    [Fact]
    public void ExecutedFileWithEmptyAnswerIsMissed()
    {
        FileAnswer[] files =
        [
            Answer("a.cs", FileCoverage.Covered),
            Answer("b.cs", FileCoverage.Covered, Layer.Discovery, Layer.WholeAssembly, Layer.DiscoveryWithLoads),
            Answer("c.cs", FileCoverage.Covered, Layer.References, Layer.Discovery, Layer.WholeAssembly, Layer.DiscoveryWithLoads),
            Answer("d.cs", FileCoverage.NotCovered),
            Answer("e.cs", FileCoverage.NotInReport),
        ];

        var summary = CoverageComparison.Summarize(files);

        Assert.Equal(new CoverageSplit(5, 3, 1, 1), summary.WithLevelZero);
        Assert.Equal(new CoverageSplit(4, 2, 1, 1), summary.Layers[Layer.References].EmptyAnswer);
        Assert.Equal(new CoverageSplit(1, 1, 0, 0), summary.Layers[Layer.References].NonEmptyAnswer);
        Assert.Equal(0.6667, summary.Layers[Layer.References].MissedShareOfCovered);
        Assert.Equal(0.6667, summary.Layers[Layer.References].CoveredShareOfEmpty);
        Assert.Equal(new CoverageSplit(3, 1, 1, 1), summary.Layers[Layer.Discovery].EmptyAnswer);
        Assert.Equal(0.3333, summary.Layers[Layer.Discovery].MissedShareOfCovered);
        Assert.Equal(0.5, summary.Layers[Layer.Discovery].CoveredShareOfEmpty);
        Assert.Equal(new CoverageSplit(3, 1, 1, 1), summary.Layers[Layer.DiscoveryWithLoads].EmptyAnswer);
        Assert.Equal(["a.cs", "b.cs"], CoverageComparison.Missed(files, Layer.References).Select(file => file.Path));
        Assert.Equal(["a.cs"], CoverageComparison.Missed(files, Layer.WholeAssembly).Select(file => file.Path));
    }

    [Fact]
    public void FileOfAssemblyWithoutTestsIsJudgedApart()
    {
        FileAnswer[] files =
        [
            new("module.cs", "Module", FileCoverage.Covered, HasLevelZero: false, new HashSet<Layer> { Layer.DiscoveryWithLoads }),
            new("other.cs", "Module", FileCoverage.Covered, HasLevelZero: false, new HashSet<Layer>()),
            Answer("a.cs", FileCoverage.NotCovered),
        ];

        var summary = CoverageComparison.Summarize(files);

        Assert.Equal(new CoverageSplit(2, 2, 0, 0), summary.InAssembliesWithoutTests);
        Assert.Equal(new CoverageSplit(1, 0, 1, 0), summary.WithLevelZero);
        Assert.Equal(0, summary.Layers[Layer.Discovery].MissedShareOfCovered);
        Assert.Equal(1, summary.LayersInAssembliesWithoutTests[Layer.Discovery].MissedShareOfCovered);
        Assert.Equal(new CoverageSplit(1, 1, 0, 0), summary.LayersInAssembliesWithoutTests[Layer.DiscoveryWithLoads].EmptyAnswer);
        Assert.Equal(0.5, summary.LayersInAssembliesWithoutTests[Layer.DiscoveryWithLoads].MissedShareOfCovered);
    }

    private static FileAnswer Answer(string path, FileCoverage coverage, params Layer[] answering) =>
        new(path, "Shop", coverage, HasLevelZero: true, answering.ToHashSet());
}
