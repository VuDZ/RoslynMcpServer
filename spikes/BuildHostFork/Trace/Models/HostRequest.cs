namespace BuildHostFork.Trace;

/// <summary>
/// One request a host served: from the moment it read the request line to the end of sending the response, and the
/// project path it concerned when that is known. <see cref="SendStart"/> is where the host began to serialize and
/// send the response: the client cannot have read it before then, while it can finish reading before the host's
/// write and flush return at <see cref="Sent"/>.
/// </summary>
internal sealed record HostRequest(HostSpan Span, string Method, string? Path, long Received, long SendStart, long Sent);
