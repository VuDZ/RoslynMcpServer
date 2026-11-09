namespace AffectedTestsProbe;

internal sealed record PairJudgment(PairStatus Status, IReadOnlyList<string> FailedUnits);
