namespace AffectedTestsProbe;

/// <summary>How one test process ended, before case rows are compared.</summary>
internal readonly record struct RunSignals(
    bool TimedOut,
    bool BuildFailed,
    bool Crashed,
    bool MissingArtifacts,
    int Executed,
    bool ZeroExecuted);
