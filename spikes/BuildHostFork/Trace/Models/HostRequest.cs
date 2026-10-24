namespace BuildHostFork.Trace;

/// <summary>
/// One request a host served: from the moment it read the request line to the end of sending the response, and the
/// project path it concerned when that is known.
/// </summary>
internal sealed record HostRequest(HostSpan Span, string Method, string? Path, long Received, long Sent);
