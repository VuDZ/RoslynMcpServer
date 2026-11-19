using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class RecallComparisonTests
{
    // Theory data on purpose: the search for tests computes it in every run of this assembly, so a run of any
    // other class executes the constructors below. That is the case the idle run of class-coverage records,
    // and it shows when the probe is checked on itself. The rows are objects because the record is internal.
    public static IEnumerable<object[]> LosingAnswers =>
    [
        [new SelectorRecall(Selected: 1, Kept: 1, KeptMethods: 5, Lost: [1], LostOutsideIdleRun: []), 0, 1],
        [new SelectorRecall(Selected: 1, Kept: 1, KeptMethods: 5, Lost: [1], LostOutsideIdleRun: [1]), 1, 0],
    ];

    [Fact]
    public void AnswerIsJudgedByTheExecutingClassesItHolds()
    {
        // Test classes are nodes 1, 2 and 3 with 10, 5 and 1 test methods.
        int[] methods = [0, 10, 5, 1];

        var recall = RecallComparison.Measure(executing: [1, 2], outsideIdleRun: new HashSet<int> { 1, 2 }, selected: new HashSet<int> { 2, 3 }, methods);

        Assert.Equal(2, recall.Selected);
        Assert.Equal(1, recall.Kept);
        Assert.Equal(5, recall.KeptMethods);
        Assert.Equal([1], recall.Lost);
        Assert.Equal([1], recall.LostOutsideIdleRun);
    }

    [Fact]
    public void ClassLostInsideTheIdleRunIsCountedApart()
    {
        int[] methods = [0, 10, 5, 1];

        // The test assembly of class 1 executes the file with no test selected; that of class 2 does not.
        var recall = RecallComparison.Measure(executing: [1, 2], outsideIdleRun: new HashSet<int> { 2 }, selected: new HashSet<int>(), methods);

        Assert.Equal([1, 2], recall.Lost);
        Assert.Equal([2], recall.LostOutsideIdleRun);
    }

    [Fact]
    public void SummaryCountsPairsOverExecutedFilesAndPrecisionOverAllAnswers()
    {
        FileRecall[] files =
        [
            File("complete.cs", executing: 2, outsideIdleRun: 2, methods: 15, new SelectorRecall(Selected: 4, Kept: 2, KeptMethods: 15, Lost: [], LostOutsideIdleRun: [])),
            File("losing.cs", executing: 2, outsideIdleRun: 2, methods: 15, new SelectorRecall(Selected: 1, Kept: 1, KeptMethods: 5, Lost: [1], LostOutsideIdleRun: [1])),
            File("empty.cs", executing: 1, outsideIdleRun: 1, methods: 10, new SelectorRecall(Selected: 0, Kept: 0, KeptMethods: 0, Lost: [1], LostOutsideIdleRun: [1])),
            File("unused.cs", executing: 0, outsideIdleRun: 0, methods: 0, new SelectorRecall(Selected: 3, Kept: 0, KeptMethods: 0, Lost: [], LostOutsideIdleRun: [])),
        ];

        var counts = RecallComparison.Summarize(files)["discovery"];

        Assert.Equal(3, counts.FilesExecuted);
        Assert.Equal(1, counts.FilesComplete);
        Assert.Equal(2, counts.FilesLosing);
        Assert.Equal(2, counts.FilesLosingOutsideIdleRun);
        Assert.Equal(1, counts.FilesWithEmptyAnswer);
        Assert.Equal(5, counts.Pairs);
        Assert.Equal(3, counts.KeptPairs);
        Assert.Equal(5, counts.PairsOutsideIdleRun);
        Assert.Equal(3, counts.KeptPairsOutsideIdleRun);
        Assert.Equal(8, counts.SelectedPairs);
        Assert.Equal(0.6, counts.Recall);
        Assert.Equal(0.6, counts.RecallOutsideIdleRun);
        Assert.Equal(0.5, counts.RecallByMethods);
        Assert.Equal(0.375, counts.Precision);
        Assert.Equal(3, counts.RecallByFile.Count);
        Assert.Equal(1, counts.RecallByFile.Zero);
        Assert.Equal(3, counts.PrecisionByFile.Count);
    }

    [Theory]
    [MemberData(nameof(LosingAnswers))]
    public void SummaryLeavesOutThePairsOfTheIdleRun(object losing, int filesLosingOutsideIdleRun, int executingInsideIdleRun)
    {
        // losing.cs is executed by two classes; the answer keeps one. Whether the lost one counts outside the
        // idle run depends on its test assembly executing the file with no test selected.
        FileRecall[] files =
        [
            File("complete.cs", executing: 2, outsideIdleRun: 2, methods: 15, new SelectorRecall(Selected: 4, Kept: 2, KeptMethods: 15, Lost: [], LostOutsideIdleRun: [])),
            File("losing.cs", executing: 2, outsideIdleRun: 2 - executingInsideIdleRun, methods: 15, (SelectorRecall)losing),
        ];

        var counts = RecallComparison.Summarize(files)["discovery"];

        Assert.Equal(1, counts.FilesLosing);
        Assert.Equal(filesLosingOutsideIdleRun, counts.FilesLosingOutsideIdleRun);
        Assert.Equal(4, counts.Pairs);
        Assert.Equal(3, counts.KeptPairs);
        Assert.Equal(4 - executingInsideIdleRun, counts.PairsOutsideIdleRun);
        Assert.Equal(3, counts.KeptPairsOutsideIdleRun);
        Assert.Equal(0.75, counts.Recall);
        Assert.Equal(executingInsideIdleRun == 1 ? 1 : 0.75, counts.RecallOutsideIdleRun);
    }

    private static FileRecall File(string path, int executing, int outsideIdleRun, int methods, SelectorRecall discovery) =>
        new(path, "Shop", executing, outsideIdleRun, methods, new Dictionary<string, SelectorRecall> { ["discovery"] = discovery });
}
