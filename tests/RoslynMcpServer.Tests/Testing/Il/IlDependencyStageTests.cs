using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>What a test class reaches at each of the three stages.</summary>
[Collection(IlShopCollection.Name)]
public sealed class IlDependencyStageTests
{
    public IlDependencyStageTests(IlShopFixture shop)
    {
        _shop = shop;
    }

    [Fact]
    public void References_reach_a_type_through_a_chain_of_edges_and_not_an_unrelated_type()
    {
        // ReferenceOnlyTests names OrderConsumer, which names IOrderService.
        Assert.True(Reaches("ReferenceOnlyTests", IlShopFixture.Lib("OrderConsumer"), IlDependencyStage.References));
        Assert.True(Reaches("ReferenceOnlyTests", IlShopFixture.Lib("IOrderService"), IlDependencyStage.References));
        Assert.False(Reaches("ReferenceOnlyTests", IlShopFixture.Lib("UnrelatedService"), IlDependencyStage.References));
        Assert.False(Reaches("ReferenceOnlyTests", IlShopFixture.Lib("OrderService"), IlDependencyStage.References));
    }

    [Fact]
    public void Explicit_registration_reaches_the_implementation_by_references()
    {
        Assert.True(Reaches("RegistrationTests", IlShopFixture.Lib("OrderService"), IlDependencyStage.References));
    }

    [Fact]
    public void Test_without_a_container_does_not_reach_the_implementation_at_any_stage()
    {
        Assert.All(
            Enum.GetValues<IlDependencyStage>(),
            stage => Assert.False(Reaches("ReferenceOnlyTests", IlShopFixture.Lib("OrderService"), stage)));
    }

    [Theory]
    [InlineData("ContainerTests")]
    [InlineData("GenericContainerTests")]
    public void Assembly_handed_to_reflection_gives_implementations_of_a_reached_type_only_with_discovery(string test)
    {
        // ContainerTests reaches typeof(X).Assembly, GenericContainerTests reaches FromAssemblyOf<T>().
        Assert.False(Reaches(test, IlShopFixture.Lib("OrderService"), IlDependencyStage.References));
        Assert.True(Reaches(test, IlShopFixture.Lib("OrderService"), IlDependencyStage.Discovery));
    }

    [Theory]
    [InlineData("ContainerTests")]
    [InlineData("GenericContainerTests")]
    public void Assembly_handed_to_reflection_gives_types_that_face_a_framework(string test)
    {
        Assert.True(_shop.Result.IsFrameworkFacing(IlShopFixture.Lib("FrameworkFoundHandler")));
        Assert.True(_shop.Result.IsFrameworkFacing(IlShopFixture.Lib("ExportedPart")));
        Assert.False(_shop.Result.IsFrameworkFacing(IlShopFixture.Lib("OrderService")));

        Assert.False(Reaches(test, IlShopFixture.Lib("FrameworkFoundHandler"), IlDependencyStage.References));
        Assert.True(Reaches(test, IlShopFixture.Lib("FrameworkFoundHandler"), IlDependencyStage.Discovery));
        Assert.True(Reaches(test, IlShopFixture.Lib("ExportedPart"), IlDependencyStage.Discovery));
    }

    [Theory]
    [InlineData("ContainerTests")]
    [InlineData("GenericContainerTests")]
    public void Type_of_a_scanned_assembly_without_a_mark_is_not_discovered(string test)
    {
        // UnrelatedService implements a contract the test does not reach and nothing marks it for a framework.
        Assert.False(Reaches(test, IlShopFixture.Lib("UnrelatedService"), IlDependencyStage.Discovery));
        Assert.False(Reaches(test, IlShopFixture.Lib("IUnrelated"), IlDependencyStage.Discovery));
    }

    [Theory]
    [InlineData("CrossAssemblyScanTests")]
    [InlineData("CrossAssemblyGenericScanTests")]
    public void Reflection_is_over_the_assembly_of_the_named_type_and_not_of_the_calling_code(string test)
    {
        // The calls typeof(OrderConsumer).Assembly and FromAssemblyOf<OrderConsumer>() are in Shop.Tests,
        // the named type is in Shop.Lib. In the other cases both are in one assembly, and a rule that took
        // the assembly of the calling code would pass them.
        Assert.Equal([IlShopFixture.LibAssembly], _shop.Result.ScansOf(IlShopFixture.Test(test)));

        Assert.False(Reaches(test, IlShopFixture.Lib("OrderService"), IlDependencyStage.References));
        Assert.True(Reaches(test, IlShopFixture.Lib("OrderService"), IlDependencyStage.Discovery));
        Assert.True(Reaches(test, IlShopFixture.Lib("FrameworkFoundHandler"), IlDependencyStage.Discovery));

        // The test assembly itself is not handed to reflection: its helper that faces a framework stays out.
        Assert.True(_shop.Result.IsFrameworkFacing(IlShopFixture.Test("TestSideHandler")));
        Assert.False(Reaches(test, IlShopFixture.Test("TestSideHandler"), IlDependencyStage.Discovery));
    }

    /// <summary>
    /// An implementation of a reached contract is found in two ways, by the order of the walk. The two cases
    /// are built so that the order does not depend on the numbers the types get in the graph.
    /// </summary>
    [Fact]
    public void Implementation_is_discovered_when_the_assembly_is_scanned_before_its_contract_is_reached()
    {
        // ScannerFirstTests names AssemblyScanner only. The scan happens when the walk is at the scanner;
        // IOrderService is reached later, through OrderConsumer, and then brings in its subtypes.
        Assert.False(Reaches("ScannerFirstTests", IlShopFixture.Lib("OrderService"), IlDependencyStage.References));
        Assert.True(Reaches("ScannerFirstTests", IlShopFixture.Lib("IOrderService"), IlDependencyStage.References));
        Assert.True(Reaches("ScannerFirstTests", IlShopFixture.Lib("OrderService"), IlDependencyStage.Discovery));
    }

    [Fact]
    public void Implementation_is_discovered_when_its_contract_is_reached_before_the_assembly_is_scanned()
    {
        // ContractFirstTests names IPricingPolicy only, and PolicyScanner is reachable only through the
        // signature of that contract. The walk is done with the contract before it gets to the scanner, so
        // the scan itself has to notice that PricingPolicy implements a type that is already reached.
        Assert.False(Reaches("ContractFirstTests", IlShopFixture.Lib("PricingPolicy"), IlDependencyStage.References));
        Assert.True(Reaches("ContractFirstTests", IlShopFixture.Lib("PolicyScanner"), IlDependencyStage.References));
        Assert.True(Reaches("ContractFirstTests", IlShopFixture.Lib("PricingPolicy"), IlDependencyStage.Discovery));
    }

    [Fact]
    public void Entry_assembly_is_neither_reflection_over_an_assembly_nor_a_load_by_name()
    {
        var test = IlShopFixture.Test("EntryAssemblyTests");
        var references = _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.References);

        Assert.Contains(IlShopFixture.Lib("EntryAssemblyReader"), references);
        Assert.Equal(references, _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.Discovery));
        Assert.Equal(references, _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.DiscoveryWithLoads));
        Assert.DoesNotContain(IlShopFixture.Lib("OrderService"), references);
    }

    [Theory]
    [InlineData("ModuleLoaderTests")]
    [InlineData("ContextLoaderTests")]
    [InlineData("LoadedAssemblyTests")]
    public void Test_that_reaches_a_load_by_name_depends_on_every_type_of_the_assemblies_next_to_its_test_assembly(string test)
    {
        // Assembly.Load, AssemblyLoadContext.LoadFromAssemblyPath and AppDomain.GetAssemblies. Copies of
        // Shop.Lib and Shop.Plugins lie next to Shop.Tests; no code of the test names their types below.
        var plugin = new IlTypeName(IlShopFixture.PluginsAssembly, "Shop.Plugins.ReportPlugin");
        Assert.All(
            [IlDependencyStage.References, IlDependencyStage.Discovery],
            stage =>
            {
                Assert.False(Reaches(test, plugin, stage));
                Assert.False(Reaches(test, IlShopFixture.Lib("UnrelatedService"), stage));
            });

        var reached = _shop.Result.Closures.ReachedTypes(IlShopFixture.Test(test), IlDependencyStage.DiscoveryWithLoads);
        var graph = _shop.Result.Graph;
        var nextToTests = graph.Types
            .Where((type, node) => type.Assembly is IlShopFixture.LibAssembly or IlShopFixture.PluginsAssembly or IlShopFixture.TestsAssembly
                && graph.TestMethods[node] == 0);
        Assert.Contains(plugin, reached);
        Assert.Contains(IlShopFixture.Lib("UnrelatedService"), reached);
        Assert.Empty(nextToTests.Except(reached));
    }

    [Fact]
    public void Load_by_name_does_not_reach_an_assembly_that_is_not_next_to_the_test_assembly()
    {
        // The output directory of Shop.Other holds a copy of Shop.Tests, but Shop.Other is not next to Shop.Tests.
        var other = new IlTypeName(IlShopFixture.OtherAssembly, "Shop.Other.OtherThing");

        Assert.Contains(other, _shop.Result.Graph.Types);
        Assert.False(Reaches("ModuleLoaderTests", other, IlDependencyStage.DiscoveryWithLoads));
    }

    [Theory]
    [InlineData("ReferenceOnlyTests")]
    [InlineData("RegistrationTests")]
    [InlineData("ContainerTests")]
    [InlineData("GenericContainerTests")]
    [InlineData("TestAssemblyScanTests")]
    public void Test_that_reaches_no_load_keeps_its_discovery_answer(string test)
    {
        var name = IlShopFixture.Test(test);

        Assert.Equal(
            _shop.Result.Closures.ReachedTypes(name, IlDependencyStage.Discovery),
            _shop.Result.Closures.ReachedTypes(name, IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Every_stage_includes_the_previous_one()
    {
        _shop.Result.AssertEveryStageIncludesThePrevious();
    }

    [Fact]
    public void Affected_test_classes_are_the_test_classes_that_reach_any_of_the_types()
    {
        var closures = _shop.Result.Closures;
        IlTypeName[] changed = [IlShopFixture.Lib("OrderService"), new IlTypeName("Shop.Lib", "Shop.NoSuchType")];

        var byReferences = closures.AffectedTestClasses(changed, IlDependencyStage.References);
        var byDiscovery = closures.AffectedTestClasses(changed, IlDependencyStage.Discovery);
        var byLoads = closures.AffectedTestClasses(changed, IlDependencyStage.DiscoveryWithLoads);

        Assert.Equal([IlShopFixture.Test("RegistrationTests")], byReferences);
        Assert.Equal(
            [
                IlShopFixture.Test("ContainerTests"),
                IlShopFixture.Test("CrossAssemblyGenericScanTests"),
                IlShopFixture.Test("CrossAssemblyScanTests"),
                IlShopFixture.Test("GenericContainerTests"),
                IlShopFixture.Test("RegistrationTests"),
                IlShopFixture.Test("ScannerFirstTests"),
            ],
            byDiscovery.Order(_typeOrder));
        Assert.Equal(
            [
                IlShopFixture.Test("ContainerTests"),
                IlShopFixture.Test("ContextLoaderTests"),
                IlShopFixture.Test("CrossAssemblyGenericScanTests"),
                IlShopFixture.Test("CrossAssemblyScanTests"),
                IlShopFixture.Test("GenericContainerTests"),
                IlShopFixture.Test("LoadedAssemblyTests"),
                IlShopFixture.Test("ModuleLoaderTests"),
                IlShopFixture.Test("RegistrationTests"),
                IlShopFixture.Test("ScannerFirstTests"),
            ],
            byLoads.Order(_typeOrder));
        Assert.Empty(closures.AffectedTestClasses([new IlTypeName("Shop.Lib", "Shop.NoSuchType")], IlDependencyStage.DiscoveryWithLoads));
    }

    private bool Reaches(string test, IlTypeName type, IlDependencyStage stage) =>
        _shop.Result.Closures.Reaches(IlShopFixture.Test(test), type, stage);

    private static readonly Comparer<IlTypeName> _typeOrder =
        Comparer<IlTypeName>.Create((left, right) => string.CompareOrdinal(left.Name, right.Name));

    private readonly IlShopFixture _shop;
}
