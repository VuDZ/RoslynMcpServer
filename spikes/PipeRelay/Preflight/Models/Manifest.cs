namespace PipeRelay.Preflight.Models;

internal sealed record Manifest(string Digest, IReadOnlyList<ManifestEntry> Entries);
