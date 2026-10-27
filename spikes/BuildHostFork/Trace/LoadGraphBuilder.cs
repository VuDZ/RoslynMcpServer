namespace BuildHostFork.Trace;

/// <summary>
/// Builds the observed load graph from the same run: nodes from the hosts' design-time builds and the solution entries,
/// edges with their provenance.
/// </summary>
/// <remarks>
/// Not observed: references that MSBuild resolves inside a design-time build (the ProjectReference protocol targets
/// call the MSBuild task); they appear in a binary log, not in this trace. A static ProjectGraph would not be complete
/// either, so the graph is a witness of this run.
/// </remarks>
internal static class LoadGraphBuilder
{
    public const string EvaluatedItem = "evaluated ProjectReference item";

    public const string ClientLoad = "client load during Resolve";

    public const string OutputPathQuery = "output path query during Resolve";

    public static LoadGraph Build(ClientTrace client, IReadOnlyList<HostTrace> hosts)
    {
        var spans = hosts.SelectMany(host => host.Spans).ToArray();
        var edges = ItemEdges(spans)
            .Concat(ResolveEdges(client, spans))
            .Distinct()
            .ToArray();
        var nodes = Nodes(client, spans).ToList();
        var known = nodes.Select(node => ProjectCosts.PathKey(node.Path)).ToHashSet();
        foreach (var target in edges.Select(edge => edge.To).Where(target => known.Add(ProjectCosts.PathKey(target))))
        {
            nodes.Add(new GraphNode(target, null, null, null, Built: false, SolutionEntryKind: null));
        }

        return new LoadGraph(nodes, edges);
    }

    private static IReadOnlyList<GraphNode> Nodes(ClientTrace client, HostSpan[] spans)
    {
        var contexts = spans.Where(span => span.Kind == "evaluate" && span.Path is not null)
            .GroupBy(span => ProjectCosts.PathKey(span.Path))
            .ToDictionary(group => group.Key, group => group.First());
        var entries = client.SolutionEntries.ToDictionary(entry => ProjectCosts.PathKey(entry.Path), entry => entry.Kind);
        var built = spans.Where(span => span.Kind == "build-instance" && span.Path is not null)
            .Select(span => (Path: span.Path!, Tfm: span.Text("targetFramework")))
            .Distinct()
            .Select(build =>
            {
                var context = contexts.GetValueOrDefault(ProjectCosts.PathKey(build.Path));
                return new GraphNode(
                    build.Path,
                    build.Tfm,
                    context?.Text("configuration"),
                    context?.Text("platform"),
                    Built: true,
                    entries.GetValueOrDefault(ProjectCosts.PathKey(build.Path)));
            })
            .ToList();
        var builtPaths = built.Select(node => ProjectCosts.PathKey(node.Path)).ToHashSet();
        built.AddRange(client.SolutionEntries
            .Where(entry => !builtPaths.Contains(ProjectCosts.PathKey(entry.Path)))
            .Select(entry => new GraphNode(entry.Path, null, null, null, Built: false, entry.Kind)));
        return built;
    }

    private static IEnumerable<GraphEdge> ItemEdges(HostSpan[] spans)
    {
        foreach (var extract in spans.Where(span => span.Kind == "extract" && span.Path is not null))
        {
            var directory = Path.GetDirectoryName(extract.Path!)!;
            foreach (var reference in extract.Texts("projectReferences"))
            {
                var referenceOutputAssembly = !reference.StartsWith("roa=false|", StringComparison.Ordinal);
                var target = referenceOutputAssembly ? reference : reference["roa=false|".Length..];
                yield return new GraphEdge(
                    extract.Path!,
                    extract.Text("targetFramework"),
                    Path.GetFullPath(Path.Combine(directory, target)),
                    EvaluatedItem,
                    referenceOutputAssembly);
            }
        }
    }

    // The client resolves references of one project at a time; an Evaluate or an output path query inside a Resolve
    // belongs to the innermost Resolve around it.
    private static IEnumerable<GraphEdge> ResolveEdges(ClientTrace client, HostSpan[] spans)
    {
        var resolves = client.Operations.Where(operation => operation.Operation == "Resolve").ToArray();
        ClientOperation? Around(long start, long end) => resolves
            .Where(resolve => resolve.Start <= start && end <= resolve.End)
            .MaxBy(resolve => resolve.Start);

        foreach (var evaluate in client.Operations.Where(operation => operation.Operation == "Evaluate"))
        {
            if (Around(evaluate.Start, evaluate.End) is { } resolve)
            {
                yield return new GraphEdge(resolve.Path, resolve.TargetFramework, evaluate.Path, ClientLoad, ReferenceOutputAssembly: null);
            }
        }

        foreach (var query in spans.Where(span => span.Kind == "output-path" && span.Path is not null))
        {
            if (Around(query.Start, query.End) is { } resolve)
            {
                yield return new GraphEdge(resolve.Path, resolve.TargetFramework, query.Path!, OutputPathQuery, ReferenceOutputAssembly: null);
            }
        }
    }
}
