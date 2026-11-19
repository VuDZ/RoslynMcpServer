namespace TestImpactIlProbe;

/// <summary>
/// Compares the answers with coverage collected per test class: for a file, the classes that execute it are
/// the reference, and an answer is judged by which of them it holds.
/// </summary>
internal static class RecallComparison
{
    public const string LevelZero = "levelZero";

    /// <param name="executing">For each production file, the test classes that execute it.</param>
    /// <param name="idle">For each production file, the test assemblies whose idle run executes it.</param>
    /// <param name="measured">Test classes with a coverage report; an answer is cut down to them.</param>
    public static List<FileRecall> Collect(
        DependencyGraph graph,
        ImpactTable table,
        ProductionCode production,
        IReadOnlyList<IReadOnlyCollection<int>> executing,
        IReadOnlyList<IReadOnlySet<int>> idle,
        IReadOnlySet<int> measured)
    {
        var rows = new FileRecall[production.Files.Count];
        Parallel.For(0, rows.Length, index =>
        {
            var (path, types) = production.Files[index];
            HashSet<int> outsideIdleRun = [.. executing[index].Where(test => !idle[index].Contains(graph.AssemblyOf[test]))];
            var selectors = new Dictionary<string, SelectorRecall>
            {
                [LevelZero] = Measure(executing[index], outsideIdleRun, Measured(table.LevelZero(types)), graph.TestMethods),
            };
            foreach (var layer in Enum.GetValues<Layer>())
            {
                selectors[MeasureReport.Key(layer)] = Measure(executing[index], outsideIdleRun, Measured(table.Affected(layer, types)), graph.TestMethods);
            }

            rows[index] = new FileRecall(
                path,
                graph.Assemblies[graph.AssemblyOf[types[0]]],
                executing[index].Count,
                outsideIdleRun.Count,
                executing[index].Sum(test => graph.TestMethods[test]),
                selectors);
        });
        return [.. rows];

        HashSet<int> Measured(List<int> tests) => [.. tests.Where(measured.Contains)];
    }

    /// <param name="outsideIdleRun">Of the executing classes, those whose test assembly does not execute the file in its idle run.</param>
    public static SelectorRecall Measure(
        IReadOnlyCollection<int> executing,
        IReadOnlySet<int> outsideIdleRun,
        IReadOnlySet<int> selected,
        IReadOnlyList<int> testMethods)
    {
        var kept = executing.Where(selected.Contains).ToList();
        List<int> lost = [.. executing.Where(test => !selected.Contains(test)).Order()];
        return new SelectorRecall(
            selected.Count,
            kept.Count,
            kept.Sum(test => testMethods[test]),
            lost,
            [.. lost.Where(outsideIdleRun.Contains)]);
    }

    public static Dictionary<string, RecallCounts> Summarize(IReadOnlyCollection<FileRecall> files)
    {
        var executed = files.Where(file => file.Executing > 0).ToList();
        return files.SelectMany(file => file.Selectors.Keys).Distinct().ToDictionary(selector => selector, selector => new RecallCounts(
            executed.Count,
            executed.Count(file => file.Selectors[selector].Lost.Count == 0),
            executed.Count(file => file.Selectors[selector].Lost.Count > 0),
            executed.Count(file => file.Selectors[selector].LostOutsideIdleRun.Count > 0),
            executed.Count(file => file.Selectors[selector].Selected == 0),
            executed.Sum(file => (long)file.Executing),
            executed.Sum(file => (long)file.Selectors[selector].Kept),
            executed.Sum(file => (long)file.ExecutingOutsideIdleRun),
            executed.Sum(file => (long)file.ExecutingOutsideIdleRun - file.Selectors[selector].LostOutsideIdleRun.Count),
            files.Sum(file => (long)file.Selectors[selector].Selected),
            executed.Sum(file => (long)file.ExecutingMethods),
            executed.Sum(file => (long)file.Selectors[selector].KeptMethods),
            Distribution.Of(executed.Select(file => (double)file.Selectors[selector].Kept / file.Executing)),
            Distribution.Of(files
                .Where(file => file.Selectors[selector].Selected > 0)
                .Select(file => (double)file.Selectors[selector].Kept / file.Selectors[selector].Selected))));
    }
}
