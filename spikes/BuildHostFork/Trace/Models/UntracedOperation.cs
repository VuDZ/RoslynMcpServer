namespace BuildHostFork.Trace;

/// <summary>
/// A client Evaluate or Build operation for which no traced host served a request of its method and path, so its
/// project went to a host that does not trace, such as the package's .NET Framework host.
/// </summary>
internal sealed record UntracedOperation(string Operation, string Path);
