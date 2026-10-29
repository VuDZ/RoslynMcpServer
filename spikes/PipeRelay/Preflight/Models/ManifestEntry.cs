namespace PipeRelay.Preflight.Models;

internal sealed record ManifestEntry(string Path, long Length, long LastWriteTicks, string Sha256);
