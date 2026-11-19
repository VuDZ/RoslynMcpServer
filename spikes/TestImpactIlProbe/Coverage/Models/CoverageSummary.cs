namespace TestImpactIlProbe;

/// <summary>
/// The comparison of the answers with coverage. Layers are judged on the files level 0 has tests for, as in
/// the measurement; files of assemblies no test assembly references are counted apart, because only a layer
/// that follows loads by name can select a test for them.
/// </summary>
internal sealed record CoverageSummary(
    CoverageSplit WithLevelZero,
    IReadOnlyDictionary<Layer, AnswerCoverage> Layers,
    CoverageSplit InAssembliesWithoutTests,
    IReadOnlyDictionary<Layer, AnswerCoverage> LayersInAssembliesWithoutTests);
