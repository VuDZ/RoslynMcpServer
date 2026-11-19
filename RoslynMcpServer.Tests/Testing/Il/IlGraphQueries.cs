using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>Questions the IL graph tests ask a built graph, by type name instead of by node number.</summary>
internal static class IlGraphQueries
{
    /// <summary>Direct dependencies of a type. Fails when the type is not a node of the graph.</summary>
    public static IReadOnlyList<IlTypeName> DependenciesOf(this IlGraphBuildResult result, IlTypeName type)
    {
        var graph = result.Graph;
        Assert.True(graph.NodeByType.TryGetValue(type, out var node), $"{type} is not in the graph.");
        return [.. graph.Dependencies[node].Select(dependency => graph.Types[dependency])];
    }

    /// <summary>Names of the assemblies the code of a type hands to reflection.</summary>
    public static IReadOnlyList<string> ScansOf(this IlGraphBuildResult result, IlTypeName type)
    {
        var graph = result.Graph;
        Assert.True(graph.NodeByType.TryGetValue(type, out var node), $"{type} is not in the graph.");
        return [.. graph.Scans[node].Select(assembly => graph.Assemblies[assembly])];
    }

    public static bool IsFrameworkFacing(this IlGraphBuildResult result, IlTypeName type) =>
        result.Graph.FrameworkFacing[result.Graph.NodeByType[type]];

    public static int TestMethodsOf(this IlGraphBuildResult result, IlTypeName type) =>
        result.Graph.TestMethods[result.Graph.NodeByType[type]];

    /// <summary>
    /// Checks on every test class of the graph that a stage answers with everything the previous stage
    /// answers with.
    /// </summary>
    public static void AssertEveryStageIncludesThePrevious(this IlGraphBuildResult result)
    {
        Assert.NotEmpty(result.Closures.TestClasses);
        foreach (var test in result.Closures.TestClasses)
        {
            var references = result.Closures.ReachedTypes(test, IlDependencyStage.References);
            var discovery = result.Closures.ReachedTypes(test, IlDependencyStage.Discovery);
            var loads = result.Closures.ReachedTypes(test, IlDependencyStage.DiscoveryWithLoads);

            Assert.Contains(test, references);
            Assert.Empty(references.Except(discovery));
            Assert.Empty(discovery.Except(loads));
        }
    }
}
