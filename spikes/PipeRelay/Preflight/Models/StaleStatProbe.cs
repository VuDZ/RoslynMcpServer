namespace PipeRelay.Preflight.Models;

/// <summary>Result of changing bytes while keeping size and modification time.</summary>
internal sealed record StaleStatProbe(string File, bool StatStampEqual, bool ContentHashEqual);
