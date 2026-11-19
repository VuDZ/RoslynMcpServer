namespace TestImpactIlProbe;

/// <summary>The answer for one edited file: the tests each layer selects and what level 0 selects.</summary>
internal sealed record TargetResult(
    string Source,
    string Path,
    IReadOnlyList<int> Types,
    List<int> LevelZero,
    IReadOnlyDictionary<Layer, List<int>> Affected);
