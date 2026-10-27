namespace BuildHostFork.Trace;

/// <summary>Per-project host and client costs, and the inventory of repeated work.</summary>
internal static class ProjectCosts
{
    public static IReadOnlyList<ProjectCost> Compute(ClientTrace client, IReadOnlyList<HostTrace> hosts)
    {
        var spans = hosts.SelectMany(host => host.Spans).Where(span => span.Path is not null).ToArray();
        var responses = hosts.SelectMany(host => ResponseSpans(host)).ToArray();
        var paths = spans.Select(span => span.Path!)
            .Concat(client.Operations.Select(operation => operation.Path))
            .Distinct(PathComparer)
            .ToArray();
        return paths.Select(path => Cost(path, client, spans, responses)).ToArray();
    }

    public static RepeatInventory Repeats(IReadOnlyList<HostTrace> hosts, long frequency)
    {
        var spans = hosts.SelectMany(host => host.Spans).ToArray();
        var evaluations = spans.Where(span => span.Kind == "evaluate").GroupBy(span => span.Path ?? "", PathComparer).Where(group => group.Count() > 1).ToArray();
        var builds = spans.Where(span => span.Kind == "build-instance")
            .GroupBy(span => PathKey(span.Path) + "|" + span.Text("targetFramework"), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        var outputQueries = spans.Where(span => span.Kind == "output-path").ToArray();
        var cachedLoads = spans.Where(span => span.Kind == "load-project" && span.Flag("cached")).ToArray();
        return new RepeatInventory(
            evaluations.Length,
            evaluations.Sum(group => group.OrderBy(span => span.Start).Skip(1).Sum(span => Ms(span, frequency))),
            cachedLoads.Length,
            builds.Sum(group => group.Count() - 1),
            builds.Sum(group => group.OrderBy(span => span.Start).Skip(1).Sum(span => Ms(span, frequency))),
            outputQueries.Length,
            outputQueries.Count(query => cachedLoads.Any(load => load.Parent == query.Id)));
    }

    public static double Ms(HostSpan span, long frequency)
        => (span.End - span.Start) * 1000.0 / frequency;

    public static string PathKey(string? path)
        => OperatingSystem.IsWindows() ? (path ?? "").ToUpperInvariant() : path ?? "";

    private static ProjectCost Cost(string path, ClientTrace client, HostSpan[] spans, (string Path, double Ms, long Chars)[] responses)
    {
        var own = spans.Where(span => PathComparer.Equals(span.Path, path)).ToArray();
        var operations = client.Operations.Where(operation => PathComparer.Equals(operation.Path, path)).ToArray();
        var ownResponses = responses.Where(response => PathComparer.Equals(response.Path, path)).ToArray();
        double Sum(string kind) => own.Where(span => span.Kind == kind).Sum(span => Ms(span, client.Frequency));
        int Count(string kind) => own.Count(span => span.Kind == kind);
        double Client(string operation) => operations
            .Where(item => item.Operation == operation)
            .Sum(item => (item.End - item.Start) * 1000.0 / client.Frequency);
        return new ProjectCost(
            path,
            Sum("evaluate"),
            Count("evaluate"),
            Sum("reevaluate"),
            Count("reevaluate"),
            Sum("msbuild-build"),
            Count("msbuild-build"),
            Sum("create-instance"),
            Sum("extract"),
            ownResponses.Sum(response => response.Ms),
            ownResponses.Sum(response => response.Chars),
            Client("Evaluate"),
            Client("Build"),
            Client("Resolve"));
    }

    // Serialization and sending of GetProjectFileInfosAsync, the request whose response carries the ProjectFileInfo.
    private static IEnumerable<(string Path, double Ms, long Chars)> ResponseSpans(HostTrace host)
    {
        var children = host.Spans.ToLookup(span => span.Parent);
        foreach (var request in HostRequests.Read(host).Where(request => request.Method == "GetProjectFileInfosAsync" && request.Path is not null))
        {
            var parts = children[request.Span.Id].Where(span => span.Kind is "rpc-serialize" or "rpc-send").ToArray();
            yield return (
                request.Path!,
                parts.Sum(span => Ms(span, host.Frequency)),
                parts.Select(span => span.Number("responseChars") ?? 0).Sum());
        }
    }

    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
