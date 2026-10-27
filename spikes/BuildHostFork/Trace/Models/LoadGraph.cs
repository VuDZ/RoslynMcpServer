namespace BuildHostFork.Trace;

/// <summary>The observed load graph of one traced load; it is a witness of this run, not a complete project graph.</summary>
internal sealed record LoadGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
