namespace PipeRelay.Transport.Models;

internal sealed record Handshake(int Version, string Identity, string Key, string HarnessId, Guid Generation);
