using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Writes a built graph as sorted lines of names. Two graphs are equal when their descriptions are equal,
/// whatever numbers the nodes got.
/// </summary>
internal static class IlGraphDescription
{
    public static IReadOnlyList<string> Describe(IlGraphBuildResult result)
    {
        var graph = result.Graph;
        var lines = new List<string> { $"unreadable bodies: {graph.UnreadableBodies}" };
        for (var assembly = 0; assembly < graph.Assemblies.Length; assembly++)
        {
            lines.Add($"assembly {graph.Assemblies[assembly]}: neighbors [{Names(graph.Neighbors[assembly].Select(index => graph.Assemblies[index]))}]");
        }

        for (var node = 0; node < graph.Types.Length; node++)
        {
            lines.Add(
                $"type {graph.Types[node]}: assembly {graph.Assemblies[graph.AssemblyOf[node]]}; "
                + $"dependencies [{Types(graph, graph.Dependencies[node])}]; "
                + $"supertypes [{Types(graph, graph.Supertypes[node])}]; "
                + $"subtypes [{Types(graph, graph.Subtypes[node])}]; "
                + $"scans [{Names(graph.Scans[node].Select(index => graph.Assemblies[index]))}]; "
                + $"loads {graph.Loads[node]}; framework-facing {graph.FrameworkFacing[node]}; "
                + $"test methods {graph.TestMethods[node]}");
        }

        foreach (var test in result.Closures.TestClasses)
        {
            foreach (var stage in Enum.GetValues<IlDependencyStage>())
            {
                lines.Add($"reach {stage} {test}: [{Names(result.Closures.ReachedTypes(test, stage).Select(type => type.ToString()))}]");
            }
        }

        lines.AddRange(result.Unread.Select(unread => $"unread {unread.Path}: {unread.Reason}"));
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static string Types(IlDependencyGraph graph, IEnumerable<int> nodes) =>
        Names(nodes.Select(node => graph.Types[node].ToString()));

    private static string Names(IEnumerable<string> names) =>
        string.Join("; ", names.Order(StringComparer.Ordinal));
}
