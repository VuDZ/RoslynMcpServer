namespace BuildHostFork.Trace;

/// <summary>
/// Splits the client's load window by what was running at each moment: a BuildHost span's own time (minus its child
/// spans), else the innermost loader operation with no host work under it, else other client work. The 5.9.0 loader
/// sends one request at a time, so the parts add up to the wall time and form its critical path; moments with more than
/// one host span running are counted separately instead of being split.
/// </summary>
internal static class WallBreakdown
{
    public const string HostOverlap = "several host spans at once";

    public const string ClientOther = "client: between loader operations";

    public const string ClientBefore = "client: before the first loader operation";

    public const string ClientAfter = "client: after the last loader operation";

    public static IReadOnlyDictionary<string, double> Compute(ClientTrace client, IReadOnlyList<HostTrace> hosts)
    {
        var segments = hosts.SelectMany(HostSegments)
            .Concat(client.Operations.Select(operation => new TimeSegment(
                operation.Start,
                operation.End,
                operation.Operation == "Resolve" ? "client: Resolve, no host work" : $"client: {operation.Operation} waiting, no host work",
                IsHost: false)))
            .Concat(ClientEdges(client))
            .Select(segment => segment with { Start = Math.Max(segment.Start, client.LoadStart), End = Math.Min(segment.End, client.LoadEnd) })
            .Where(segment => segment.End > segment.Start)
            .ToArray();
        return Sweep(segments, client.LoadStart, client.LoadEnd, client.Frequency);
    }

    public static string Category(string kind)
        => kind switch
        {
            "evaluate" => "host: evaluation (root)",
            "reevaluate" => "host: evaluation (TFM switch)",
            "msbuild-build" => "host: design-time build",
            "create-instance" => "host: project instance",
            "extract" => "host: ProjectFileInfo extraction",
            "extract-compiler-args" => "host: extraction / compiler arguments",
            "extract-fallback-args" => "host: extraction / fallback arguments",
            "extract-properties" => "host: extraction / properties",
            "extract-documents" => "host: extraction / documents and metadata",
            "extract-additional-editorconfig" => "host: extraction / additional and editorconfig documents",
            "extract-items" => "host: extraction / other items and properties",
            "extract-globs" => "host: extraction / GetAllGlobs",
            "extract-result" => "host: extraction / result and project references",
            "load-project" => "host: project file read",
            "output-path" => "host: output path query",
            "build-project" or "build-instance" => "host: build bookkeeping",
            "create-build-manager" => "host: build manager start",
            "rpc-invoke" => "host: RPC method, other",
            "rpc" => "host: RPC dispatch",
            "rpc-queue" => "host: RPC queue",
            "rpc-serialize" => "host: response serialization",
            "rpc-send" => "host: response send",
            "connect" => "host: waiting for the client to connect",
            "startup" => "host: process startup (estimate)",
            _ => "host: " + kind,
        };

    internal static IReadOnlyDictionary<string, double> Sweep(IReadOnlyList<TimeSegment> segments, long windowStart, long windowEnd, long frequency)
    {
        var events = new List<(long Time, bool IsStart, int Index)>(segments.Count * 2);
        for (var index = 0; index < segments.Count; index++)
        {
            events.Add((segments[index].Start, true, index));
            events.Add((segments[index].End, false, index));
        }

        events.Sort((left, right) => left.Time != right.Time ? left.Time.CompareTo(right.Time) : left.IsStart.CompareTo(right.IsStart));
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        var activeHost = new List<int>();
        var activeClient = new List<int>();
        var position = windowStart;
        foreach (var (time, isStart, index) in events)
        {
            if (time > position)
            {
                Add(totals, Label(segments, activeHost, activeClient), (time - position) * 1000.0 / frequency);
                position = time;
            }

            var active = segments[index].IsHost ? activeHost : activeClient;
            if (isStart)
            {
                active.Add(index);
            }
            else
            {
                active.Remove(index);
            }
        }

        if (windowEnd > position)
        {
            Add(totals, Label(segments, activeHost, activeClient), (windowEnd - position) * 1000.0 / frequency);
        }

        return totals;
    }

    // Solution parsing and host startup come before the first operation, building the Solution from the project infos
    // after the last one. These segments start before or end after all operations, so any operation is innermost.
    private static IEnumerable<TimeSegment> ClientEdges(ClientTrace client)
    {
        if (client.Operations.Count == 0)
        {
            yield break;
        }

        yield return new TimeSegment(client.LoadStart, client.Operations.Min(operation => operation.Start), ClientBefore, IsHost: false);
        yield return new TimeSegment(client.Operations.Max(operation => operation.End), client.LoadEnd, ClientAfter, IsHost: false);
    }

    private static IEnumerable<TimeSegment> HostSegments(HostTrace host)
    {
        var children = host.Spans.ToLookup(span => span.Parent);
        foreach (var span in host.Spans)
        {
            foreach (var (start, end) in Subtract(span.Start, span.End, children[span.Id]))
            {
                yield return new TimeSegment(start, end, Category(span.Kind), IsHost: true);
            }

            if (span.Kind == "rpc" && span.Number("received") is long received && received < span.Start)
            {
                yield return new TimeSegment(received, span.Start, Category("rpc-queue"), IsHost: true);
            }
        }

        var connect = host.Spans.FirstOrDefault(span => span.Kind == "connect");
        if (connect is not null && host.ProcessStart < connect.Start)
        {
            yield return new TimeSegment(host.ProcessStart, connect.Start, Category("startup"), IsHost: true);
        }
    }

    // The span minus the union of its children; children of one async flow do not overlap, but nothing relies on it.
    private static IEnumerable<(long Start, long End)> Subtract(long start, long end, IEnumerable<HostSpan> children)
    {
        var position = start;
        foreach (var child in children.OrderBy(child => child.Start))
        {
            if (child.Start > position)
            {
                yield return (position, Math.Min(child.Start, end));
            }

            position = Math.Max(position, child.End);
            if (position >= end)
            {
                yield break;
            }
        }

        if (end > position)
        {
            yield return (position, end);
        }
    }

    // Loader operations nest (Resolve contains the loads of referenced projects), so the latest started one is innermost.
    private static string Label(IReadOnlyList<TimeSegment> segments, List<int> activeHost, List<int> activeClient)
    {
        if (activeHost.Count > 1)
        {
            return HostOverlap;
        }

        if (activeHost.Count == 1)
        {
            return segments[activeHost[0]].Label;
        }

        if (activeClient.Count == 0)
        {
            return ClientOther;
        }

        var innermost = activeClient.MaxBy(index => (segments[index].Start, -segments[index].End));
        return segments[innermost].Label;
    }

    private static void Add(Dictionary<string, double> totals, string label, double milliseconds)
        => totals[label] = totals.GetValueOrDefault(label) + milliseconds;
}
