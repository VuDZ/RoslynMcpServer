using TestImpactIlProbe.Tests.Fixtures;
using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class LoadsByNameTests
{
    [Fact]
    public void LoadByNameIsRecordedAndEntryAssemblyIsNot()
    {
        Assert.Equal(AssemblyLoadKinds.AssemblyLoad, TestGraph.Graph.Loads[TestGraph.Node(typeof(ModuleLoader))]);
        Assert.Equal(AssemblyLoadKinds.None, TestGraph.Graph.Loads[TestGraph.Node(typeof(EntryAssemblyReader))]);
    }

    [Fact]
    public void TestAssemblyIsDeployedWithAssembliesNextToIt()
    {
        var graph = TestGraph.Graph;
        var deployed = graph.Deployed[graph.AssemblyOf[TestGraph.Node<ModuleLoaderTests>()]];

        Assert.Equal(
            [typeof(MeasureCommand).Assembly.GetName().Name, TestGraph.Assembly],
            deployed.Select(assembly => graph.Assemblies[assembly]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LoadReachesEveryDeployedTypeOnlyInLayersThatFollowLoads()
    {
        var test = TestGraph.Node<ModuleLoaderTests>();

        Assert.All(
            [Layer.References, Layer.Discovery, Layer.WholeAssembly],
            layer => Assert.DoesNotContain(test, Affected(layer, typeof(MeasureCommand))));
        Assert.All(
            [Layer.ReferencesWithLoads, Layer.DiscoveryWithLoads],
            layer =>
            {
                Assert.Contains(test, Affected(layer, typeof(MeasureCommand)));
                Assert.Contains(test, Affected(layer, typeof(UnrelatedService)));
            });
    }

    [Fact]
    public void LoadByNameStillReachesEverythingInTheNarrowLayer()
    {
        Assert.Contains(TestGraph.Node<ModuleLoaderTests>(), Affected(Layer.DiscoveryWithNamedLoads, typeof(MeasureCommand)));
    }

    [Fact]
    public void LoadedAssembliesAreScannedInTheNarrowLayerAndBringEverythingInTheWideOne()
    {
        var test = TestGraph.Node<LoadedAssemblyTests>();

        Assert.Equal(AssemblyLoadKinds.LoadedAssemblies, TestGraph.Graph.Loads[TestGraph.Node(typeof(LoadedAssemblyScanner))]);
        Assert.DoesNotContain(test, Affected(Layer.Discovery, typeof(FrameworkFoundHandler)));
        Assert.Contains(test, Affected(Layer.DiscoveryWithNamedLoads, typeof(FrameworkFoundHandler)));
        Assert.DoesNotContain(test, Affected(Layer.DiscoveryWithNamedLoads, typeof(UnrelatedService)));
        Assert.DoesNotContain(test, Affected(Layer.DiscoveryWithNamedLoads, typeof(MeasureCommand)));
        Assert.Contains(test, Affected(Layer.DiscoveryWithLoads, typeof(UnrelatedService)));
        Assert.Contains(test, Affected(Layer.DiscoveryWithLoads, typeof(MeasureCommand)));
    }

    [Fact]
    public void TestThatReachesNoLoadKeepsItsNarrowAnswer()
    {
        var test = TestGraph.Node<ReferenceOnlyTests>();

        Assert.All(
            [Layer.ReferencesWithLoads, Layer.DiscoveryWithLoads, Layer.DiscoveryWithNamedLoads],
            layer => Assert.DoesNotContain(test, Affected(layer, typeof(MeasureCommand))));
    }

    [Fact]
    public void ExplainNamesTheLoadAsTheReason()
    {
        var path = TestClosures.Explain(
            TestGraph.Graph,
            Layer.ReferencesWithLoads,
            TestGraph.Node<ModuleLoaderTests>(),
            TestGraph.Node(typeof(MeasureCommand)));

        Assert.NotNull(path);
        Assert.Equal(new ReachStep(TestGraph.Node(typeof(ModuleLoader)), ReachReason.Reference), path[^2]);
        Assert.Equal(new ReachStep(TestGraph.Node(typeof(MeasureCommand)), ReachReason.LoadedByName), path[^1]);
    }

    private static List<int> Affected(Layer layer, Type type) =>
        _table.Value.Affected(layer, [TestGraph.Node(type)]);

    private static readonly Lazy<ImpactTable> _table = new(() => new ImpactTable(TestGraph.Graph));
}
