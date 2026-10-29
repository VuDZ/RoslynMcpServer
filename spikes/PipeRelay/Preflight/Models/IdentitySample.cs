namespace PipeRelay.Preflight.Models;

internal sealed record IdentitySample(string Cache, int Run, double StatScanMs, double ManifestHashMs,
    double CopyWithHashMs, double VerifyCopyMs, double ManifestReuseMs);
