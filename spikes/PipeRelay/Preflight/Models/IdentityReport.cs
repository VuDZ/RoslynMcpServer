namespace PipeRelay.Preflight.Models;

internal sealed record IdentityReport(string Source, string[] ExcludedTopLevel, int Files, long Bytes,
    string Runtime, string Os, string GcMode, int ProcessorCount, string ColdMethod,
    IReadOnlyList<IdentitySample> Samples, StaleStatProbe StaleStat);
