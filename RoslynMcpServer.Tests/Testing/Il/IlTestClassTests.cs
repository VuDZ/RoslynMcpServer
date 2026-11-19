using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>Which types of a graph are test classes and what that changes for other tests.</summary>
[Collection(IlShopCollection.Name)]
public sealed class IlTestClassTests
{
    public IlTestClassTests(IlShopFixture shop)
    {
        _shop = shop;
    }

    [Fact]
    public void Test_classes_are_the_non_abstract_types_with_test_methods_of_any_supported_framework()
    {
        string[] expected =
        [
            "ContainerTests",
            "ContextLoaderTests",
            "ContractFirstTests",
            "CrossAssemblyGenericScanTests",
            "CrossAssemblyScanTests",
            "CustomAttributeTests",
            "DerivedFixtureTests",
            "EntryAssemblyTests",
            "GenericContainerTests",
            "LoadedAssemblyTests",
            "MSTestStyleTests",
            "ModuleLoaderTests",
            "NUnitStyleTests",
            "ReferenceOnlyTests",
            "RegistrationTests",
            "ScannerFirstTests",
            "TestAssemblyScanTests",
        ];

        Assert.Equal(
            expected.Select(IlShopFixture.Test),
            _shop.Result.Closures.TestClasses.OrderBy(test => test.Name, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ReferenceOnlyTests")]
    [InlineData("NUnitStyleTests")]
    [InlineData("MSTestStyleTests")]
    public void Method_under_an_xunit_nunit_or_mstest_attribute_makes_a_test_class(string test)
    {
        Assert.Equal(1, _shop.Result.TestMethodsOf(IlShopFixture.Test(test)));
    }

    [Fact]
    public void Attribute_derived_from_a_test_attribute_in_the_solution_makes_a_test_class()
    {
        Assert.Equal(1, _shop.Result.TestMethodsOf(IlShopFixture.Test("CustomAttributeTests")));
    }

    [Fact]
    public void Abstract_class_is_not_a_test_class_and_its_heir_counts_the_inherited_test_methods()
    {
        Assert.Equal(0, _shop.Result.TestMethodsOf(IlShopFixture.Test("AbstractFixtureTests")));
        Assert.Equal(2, _shop.Result.TestMethodsOf(IlShopFixture.Test("DerivedFixtureTests")));
        Assert.Equal(0, _shop.Result.TestMethodsOf(IlShopFixture.Test("NotATest")));
        Assert.Equal(0, _shop.Result.TestMethodsOf(IlShopFixture.Lib("OrderService")));
    }

    [Fact]
    public void Reached_types_of_a_type_that_is_not_a_test_class_are_empty()
    {
        Assert.All(
            Enum.GetValues<IlDependencyStage>(),
            stage =>
            {
                Assert.Empty(_shop.Result.Closures.ReachedTypes(IlShopFixture.Test("AbstractFixtureTests"), stage));
                Assert.Empty(_shop.Result.Closures.ReachedTypes(IlShopFixture.Lib("OrderConsumer"), stage));
                Assert.Empty(_shop.Result.Closures.ReachedTypes(new IlTypeName("Shop.Lib", "Shop.NoSuchType"), stage));
            });
    }

    [Fact]
    public void Reflection_over_the_test_assembly_does_not_reach_other_test_classes()
    {
        // TestAssemblyScanTests hands its own assembly to reflection. A helper that a framework finds is
        // discovered; a test class is not, although [TestClass] marks MSTestStyleTests for a framework.
        var test = IlShopFixture.Test("TestAssemblyScanTests");
        var discovered = _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.Discovery);

        Assert.True(_shop.Result.IsFrameworkFacing(IlShopFixture.Test("MSTestStyleTests")));
        Assert.Contains(IlShopFixture.Test("TestSideHandler"), discovered);
        Assert.DoesNotContain(
            IlShopFixture.Test("TestSideHandler"),
            _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.References));
        Assert.Equal([test], discovered.Intersect(_shop.Result.Closures.TestClasses));
    }

    [Fact]
    public void Load_by_name_does_not_reach_other_test_classes()
    {
        var test = IlShopFixture.Test("ModuleLoaderTests");
        var reached = _shop.Result.Closures.ReachedTypes(test, IlDependencyStage.DiscoveryWithLoads);

        Assert.Contains(IlShopFixture.Test("NotATest"), reached);
        Assert.Equal([test], reached.Intersect(_shop.Result.Closures.TestClasses));
    }

    [Fact]
    public void Every_test_class_depends_on_the_module_initializer_and_the_setup_fixture_of_its_assembly()
    {
        Assert.All(
            _shop.Result.Closures.TestClasses,
            test =>
            {
                Assert.True(_shop.Result.Closures.Reaches(test, IlShopFixture.Lib("ModuleInitializerTarget"), IlDependencyStage.References));
                Assert.True(_shop.Result.Closures.Reaches(test, IlShopFixture.Lib("SetupTarget"), IlDependencyStage.References));
            });
    }

    private readonly IlShopFixture _shop;
}
