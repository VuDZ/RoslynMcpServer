using TestImpactIlProbe.Tests.Fixtures;

namespace TestImpactIlProbe.Tests;

/// <summary>The graph of this test assembly itself, read from its build output once per test run.</summary>
internal static class TestGraph
{
    public static DependencyGraph Graph => _graph.Value;

    public static string Assembly { get; } = typeof(OrderConsumer).Assembly.GetName().Name!;

    public static int Node<T>() => Node(typeof(T));

    public static int Node(Type type)
    {
        var node = Array.FindIndex(
            Graph.Names,
            name => name == type.FullName);
        return node >= 0 && Graph.Assemblies[Graph.AssemblyOf[node]] == type.Assembly.GetName().Name
            ? node
            : throw new InvalidOperationException($"{type.FullName} is not in the graph.");
    }

    private static DependencyGraph Load()
    {
        var directory = Path.GetDirectoryName(typeof(OrderConsumer).Assembly.Location)!;
        return GraphLoader.Load([directory], [directory]).Graph;
    }

    private static readonly Lazy<DependencyGraph> _graph = new(Load);
}
