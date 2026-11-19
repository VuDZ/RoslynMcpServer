using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class CoverageIndexTests
{
    [Fact]
    public void AbsoluteNameMatchesWhateverTheSeparatorsAndCase()
    {
        var index = Index(Report([], (@"e:\repo\src\Order.cs", true)));

        Assert.Equal(FileCoverage.Covered, index.Of(Order));
    }

    [Fact]
    public void RelativeNameIsTriedAgainstSourcesOfTheReport()
    {
        var index = Index(Report([@"D:\elsewhere", @"E:\repo\"], (@"src\Order.cs", true)));

        Assert.Equal(FileCoverage.Covered, index.Of(Order));
    }

    [Fact]
    public void RelativeAndDeterministicNamesAreTriedAgainstTheRoot()
    {
        var index = Index(Report([], ("src/Order.cs", true), ("/_/src/Unused.cs", false)));

        Assert.Equal(FileCoverage.Covered, index.Of(Order));
        Assert.Equal(FileCoverage.NotCovered, index.Of(Unused));
    }

    [Fact]
    public void ExecutionInOneReportWinsOverAnotherReport()
    {
        var index = Index(
            Report([], (Order, true), (Unused, false)),
            Report([], (Order, false), (Unused, false)));

        Assert.Equal(2, index.Reports);
        Assert.Equal(FileCoverage.Covered, index.Of(Order));
        Assert.Equal(FileCoverage.NotCovered, index.Of(Unused));
    }

    [Fact]
    public void FileOutsideTheGraphIsCountedApart()
    {
        var index = Index(Report([], (@"E:\repo\test\OrderTests.cs", true), (Order, true)));

        Assert.Equal(1, index.Matched);
        Assert.Equal([@"E:\repo\test\OrderTests.cs"], index.Unmatched);
        Assert.Equal(FileCoverage.NotInReport, index.Of(Unused));
    }

    private static CoverageIndex Index(params CoberturaReport[] reports)
    {
        var index = new CoverageIndex([Order, Unused], @"E:\repo");
        foreach (var report in reports)
        {
            index.Add(report);
        }

        return index;
    }

    private static CoberturaReport Report(string[] sources, params (string Name, bool Hit)[] files) =>
        new(sources, files.ToDictionary(file => file.Name, file => file.Hit));

    private const string Order = @"E:\repo\src\Order.cs";
    private const string Unused = @"E:\repo\src\Unused.cs";
}
