namespace AffectedTestsProbe;

/// <summary>Tests in scope, and member project keys whose level-0 tests sit outside that scope.</summary>
internal sealed record LevelZeroSplit(IReadOnlyList<TestUnit> InScope, IReadOnlyList<ProjectKey> OutsideScope);
