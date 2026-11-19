namespace TestImpactIlProbe;

/// <summary>
/// Compares the answers of the graph with coverage. Coverage of a whole test run says only whether some test
/// executes a file, so it can prove an empty answer wrong and cannot confirm a non-empty one.
/// </summary>
internal static class CoverageComparison
{
    public static List<FileAnswer> Collect(
        DependencyGraph graph,
        ImpactTable table,
        ProductionCode production,
        CoverageIndex coverage)
    {
        var layers = Enum.GetValues<Layer>();
        var rows = new FileAnswer[production.Files.Count];
        Parallel.For(0, rows.Length, index =>
        {
            var (path, types) = production.Files[index];
            rows[index] = new FileAnswer(
                path,
                graph.Assemblies[graph.AssemblyOf[types[0]]],
                coverage.Of(path),
                table.LevelZero(types).Count > 0,
                layers.Where(layer => table.Affected(layer, types).Count > 0).ToHashSet());
        });
        return [.. rows];
    }

    public static CoverageSummary Summarize(IReadOnlyCollection<FileAnswer> files)
    {
        var tested = files.Where(file => file.HasLevelZero).ToList();
        var untested = files.Where(file => !file.HasLevelZero).ToList();
        return new CoverageSummary(Split(tested), ByLayer(tested), Split(untested), ByLayer(untested));
    }

    /// <summary>Files a test executes while <paramref name="layer"/> selects no test for them.</summary>
    public static IEnumerable<FileAnswer> Missed(IEnumerable<FileAnswer> files, Layer layer) => files.Where(file =>
        file.Coverage == FileCoverage.Covered && !file.AnsweringLayers.Contains(layer));

    private static Dictionary<Layer, AnswerCoverage> ByLayer(List<FileAnswer> files) =>
        Enum.GetValues<Layer>().ToDictionary(layer => layer, layer => new AnswerCoverage(
            Split(files.Where(file => !file.AnsweringLayers.Contains(layer))),
            Split(files.Where(file => file.AnsweringLayers.Contains(layer)))));

    private static CoverageSplit Split(IEnumerable<FileAnswer> files) => CoverageSplit.Of(files.Select(file => file.Coverage));
}
