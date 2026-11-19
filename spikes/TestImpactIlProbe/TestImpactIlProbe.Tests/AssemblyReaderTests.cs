using TestImpactIlProbe.Tests.Fixtures;
using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class AssemblyReaderTests
{
    [Fact]
    public void ConsumerDependsOnContractAndNotOnImplementation()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node<OrderConsumer>()];

        Assert.Contains(TestGraph.Node(typeof(IOrderService)), dependencies);
        Assert.DoesNotContain(TestGraph.Node<OrderService>(), dependencies);
    }

    [Fact]
    public void GenericRegistrationNamesImplementation()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node(typeof(OrderRegistration))];

        Assert.Contains(TestGraph.Node<OrderService>(), dependencies);
        Assert.Contains(TestGraph.Node(typeof(IOrderService)), dependencies);
    }

    [Fact]
    public void LambdaAndAsyncBodiesBelongToDeclaringType()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node(typeof(LambdaUser))];

        Assert.Contains(TestGraph.Node(typeof(LambdaTarget)), dependencies);
        Assert.Contains(TestGraph.Node(typeof(AsyncTarget)), dependencies);
    }

    [Fact]
    public void ReferenceAfterSwitchInstructionIsRead()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node(typeof(SwitchUser))];

        Assert.Contains(TestGraph.Node(typeof(SwitchTarget)), dependencies);
        Assert.Equal(0, TestGraph.Graph.UnreadableBodies);
    }

    [Fact]
    public void TypeofArgumentOfAttributeIsDependency()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node<AttributeArgumentUser>()];

        Assert.Contains(TestGraph.Node<AttributeArgumentTarget>(), dependencies);
    }

    /// <summary>
    /// A known limit of reading IL: the compiler copies a constant into the user, so no reference remains.
    /// The tool has to treat an edited constant separately.
    /// </summary>
    [Fact]
    public void ConstantUseLeavesNoDependency()
    {
        var dependencies = TestGraph.Graph.Dependencies[TestGraph.Node(typeof(ConstantUser))];

        Assert.DoesNotContain(TestGraph.Node(typeof(ConstantHolder)), dependencies);
    }

    [Fact]
    public void TypeofAssemblyIsRecordedAsReflectionOverThatAssembly()
    {
        var scans = TestGraph.Graph.Scans[TestGraph.Node(typeof(AssemblyScanner))];

        Assert.Equal([TestGraph.Assembly], scans.Select(assembly => TestGraph.Graph.Assemblies[assembly]));
    }

    [Fact]
    public void TypeWithoutMethodBodiesHasItsFile()
    {
        var documents = TestGraph.Graph.Documents[TestGraph.Node(typeof(IOrderService))];

        Assert.Contains(documents, document => Path.GetFileName(document) == "IOrderService.cs");
    }

    [Fact]
    public void ImplementationOfPackageInterfaceIsFrameworkFacing()
    {
        Assert.True(TestGraph.Graph.FrameworkFacing[TestGraph.Node<FrameworkFoundHandler>()]);
        Assert.False(TestGraph.Graph.FrameworkFacing[TestGraph.Node<OrderService>()]);
    }

    [Fact]
    public void TestClassCountsInheritedTestMethods()
    {
        Assert.Equal(2, TestGraph.Graph.TestMethods[TestGraph.Node<DerivedFixtureTests>()]);
        Assert.Equal(0, TestGraph.Graph.TestMethods[TestGraph.Node(typeof(AbstractFixtureTests))]);
        Assert.Equal(0, TestGraph.Graph.TestMethods[TestGraph.Node<OrderService>()]);
    }
}
