namespace TestImpactIlProbe;

/// <summary>
/// One production file: its coverage, whether level 0 has tests for it, and the layers that select at least
/// one test for it.
/// </summary>
internal sealed record FileAnswer(
    string Path,
    string Assembly,
    FileCoverage Coverage,
    bool HasLevelZero,
    IReadOnlySet<Layer> AnsweringLayers);
