namespace TestImpactIlProbe;

/// <summary>
/// Hand-written code outside test assemblies: the types and files an edit of production code can touch.
/// </summary>
internal sealed class ProductionCode
{
    public ProductionCode(DependencyGraph graph)
    {
        var files = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var exists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var types = new List<int>();
        for (var node = 0; node < graph.Names.Length; node++)
        {
            if (graph.TestAssembly[graph.AssemblyOf[node]] || graph.Names[node].StartsWith('<'))
            {
                continue;
            }

            var written = false;
            foreach (var document in graph.Documents[node])
            {
                if (!exists.TryGetValue(document, out var present))
                {
                    present = !IsIntermediate(document) && File.Exists(document);
                    exists[document] = present;
                }

                if (present)
                {
                    written = true;
                    (files.TryGetValue(document, out var list) ? list : files[document] = []).Add(node);
                }
            }

            if (written)
            {
                types.Add(node);
            }
        }

        Types = types;
        Files = files.OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
            .Select(file => (file.Key, (IReadOnlyList<int>)file.Value))
            .ToList();
    }

    public IReadOnlyList<int> Types { get; }

    public IReadOnlyList<(string Path, IReadOnlyList<int> Types)> Files { get; }

    // Generated sources under obj are not what an agent edits.
    private static bool IsIntermediate(string path) =>
        path.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase);
}
