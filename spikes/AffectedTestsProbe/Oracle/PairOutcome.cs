namespace AffectedTestsProbe;

/// <summary>
/// A pair that already failed to build, finish, or produce results is not judged.
/// Empty case lists would otherwise look like a valid pair.
/// </summary>
internal static class PairOutcome
{
    public static string? Invalid(RunSignals control, RunSignals? mutation)
    {
        if (control.TimedOut || mutation?.TimedOut == true)
        {
            return "timeout";
        }

        if (control.BuildFailed || mutation?.BuildFailed == true)
        {
            return "build failed";
        }

        if (control.Crashed || mutation?.Crashed == true)
        {
            return "crash";
        }

        if (control.MissingArtifacts || mutation?.MissingArtifacts == true)
        {
            return "missing results";
        }

        if (control.ZeroExecuted || mutation?.ZeroExecuted == true || control.Executed == 0 || mutation is { Executed: 0 })
        {
            return "zero executed";
        }

        return null;
    }

    public static string? JudgmentText(string? processInvalid, PairJudgment? judgment) =>
        processInvalid is null ? judgment?.Status.ToString() : null;

    public static string? AfterJudgment(PairJudgment judgment) => judgment.Status switch
    {
        PairStatus.MissingResults => "missing results",
        PairStatus.SkippedMismatch => "skipped mismatch",
        _ => null,
    };
}
