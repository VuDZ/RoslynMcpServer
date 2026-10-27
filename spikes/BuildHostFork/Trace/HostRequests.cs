namespace BuildHostFork.Trace;

/// <summary>
/// The requests of one host with the project path each one served. LoadProjectFileAsync carries the path as its
/// argument and returns the id of the project object that later requests target; target ids are per host.
/// </summary>
internal static class HostRequests
{
    public static IReadOnlyList<HostRequest> Read(HostTrace host)
    {
        var sends = host.Spans
            .Where(span => span.Kind == "rpc-send")
            .ToDictionary(span => span.Parent);
        var targets = new Dictionary<long, string>();
        var requests = new List<HostRequest>();
        foreach (var span in host.Spans.Where(span => span.Kind == "rpc").OrderBy(span => span.Start))
        {
            var method = span.Text("method") ?? "";
            var path = span.Text("argument");
            if (method == "LoadProjectFileAsync" && path is not null && span.Number("resultInt") is long target)
            {
                targets[target] = path;
            }
            else if (path is null && span.Number("target") is long requestTarget && requestTarget != 0)
            {
                path = targets.GetValueOrDefault(requestTarget);
            }

            var send = sends.GetValueOrDefault(span.Id);
            requests.Add(new HostRequest(span, method, path, span.Number("received") ?? span.Start, send?.Start ?? span.End, send?.End ?? span.End));
        }

        return requests;
    }
}
