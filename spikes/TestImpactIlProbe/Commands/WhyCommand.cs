namespace TestImpactIlProbe;

/// <summary>Prints the chain of types through which a test class depends on a type.</summary>
internal static class WhyCommand
{
    public static int Run(string[] args)
    {
        var root = Path.GetFullPath(CommandArgs.Value(args, "--root") ?? throw new InvalidOperationException("Pass --root."));
        var bins = CommandArgs.Values(args, "--bin").Select(Path.GetFullPath).ToList();
        var testName = CommandArgs.Value(args, "--test") ?? throw new InvalidOperationException("Pass --test.");
        var typeName = CommandArgs.Value(args, "--type") ?? throw new InvalidOperationException("Pass --type.");
        var graph = GraphLoader.Load(bins.Count == 0 ? [root] : bins, [root]).Graph;
        var test = Find(graph, testName, testsOnly: true);
        var type = Find(graph, typeName, testsOnly: false);
        if (test is null || type is null)
        {
            return 1;
        }

        foreach (var layer in Enum.GetValues<Layer>())
        {
            var path = TestClosures.Explain(graph, layer, test.Value, type.Value);
            Console.WriteLine($"{layer}: {(path is null ? "not reached" : $"{path.Count - 1} steps")}");
            foreach (var step in path ?? [])
            {
                Console.WriteLine($"  {step.Reason,-18} {graph.Names[step.Node]}, {graph.Assemblies[graph.AssemblyOf[step.Node]]}");
            }
        }

        return 0;
    }

    private static int? Find(DependencyGraph graph, string name, bool testsOnly)
    {
        var matches = Enumerable.Range(0, graph.Names.Length)
            .Where(node => !testsOnly || graph.TestMethods[node] > 0)
            .Where(node => graph.Names[node] == name || graph.Names[node].EndsWith("." + name, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        Console.Error.WriteLine(matches.Count == 0 ? $"No type named {name}." : $"{name} is ambiguous:");
        foreach (var match in matches.Take(20))
        {
            Console.Error.WriteLine($"  {graph.Names[match]}, {graph.Assemblies[graph.AssemblyOf[match]]}");
        }

        return null;
    }
}
