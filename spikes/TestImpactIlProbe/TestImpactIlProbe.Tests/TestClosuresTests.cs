using TestImpactIlProbe.Tests.Fixtures;
using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class TestClosuresTests
{
    [Fact]
    public void ExplicitRegistrationReachesImplementationByReferences()
    {
        Assert.Contains(TestGraph.Node<RegistrationTests>(), Affected(Layer.References, typeof(OrderService)));
    }

    [Fact]
    public void TestWithoutContainerDoesNotReachImplementationInAnyLayer()
    {
        var test = TestGraph.Node<ReferenceOnlyTests>();

        Assert.All(Enum.GetValues<Layer>(), layer => Assert.DoesNotContain(test, Affected(layer, typeof(OrderService))));
    }

    [Fact]
    public void ScanReachesImplementationOfReachedContractOnlyWithDiscovery()
    {
        var test = TestGraph.Node<ContainerTests>();

        Assert.DoesNotContain(test, Affected(Layer.References, typeof(OrderService)));
        Assert.Contains(test, Affected(Layer.Discovery, typeof(OrderService)));
        Assert.Contains(test, Affected(Layer.WholeAssembly, typeof(OrderService)));
    }

    [Fact]
    public void ScanReachesFrameworkFacingType()
    {
        Assert.Contains(TestGraph.Node<ContainerTests>(), Affected(Layer.Discovery, typeof(FrameworkFoundHandler)));
    }

    [Fact]
    public void ImplementationOfUnreachedContractNeedsWholeAssembly()
    {
        var test = TestGraph.Node<ContainerTests>();

        Assert.DoesNotContain(test, Affected(Layer.Discovery, typeof(UnrelatedService)));
        Assert.Contains(test, Affected(Layer.WholeAssembly, typeof(UnrelatedService)));
    }

    [Fact]
    public void ScanOfTestAssemblyDoesNotReachOtherTestClasses()
    {
        var path = TestClosures.Explain(
            TestGraph.Graph,
            Layer.WholeAssembly,
            TestGraph.Node<ContainerTests>(),
            TestGraph.Node<RegistrationTests>());

        Assert.Null(path);
    }

    [Fact]
    public void ExplainNamesTheStepThatFoundTheImplementation()
    {
        var path = TestClosures.Explain(
            TestGraph.Graph,
            Layer.Discovery,
            TestGraph.Node<ContainerTests>(),
            TestGraph.Node<OrderService>());

        Assert.NotNull(path);
        Assert.Equal(TestGraph.Node<ContainerTests>(), path[0].Node);
        Assert.Equal(new ReachStep(TestGraph.Node<OrderService>(), ReachReason.ImplementsReached), path[^1]);
    }

    private static List<int> Affected(Layer layer, Type type) =>
        _table.Value.Affected(layer, [TestGraph.Node(type)]);

    private static readonly Lazy<ImpactTable> _table = new(() => new ImpactTable(TestGraph.Graph));
}
