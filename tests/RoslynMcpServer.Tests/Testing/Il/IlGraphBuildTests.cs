using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>Building a graph from values that are already read, and merging the builds of one project.</summary>
[Collection(IlShopCollection.Name)]
public sealed class IlGraphBuildTests : IDisposable
{
    public IlGraphBuildTests(IlShopFixture shop)
    {
        _shop = shop;
        _directory = Path.Combine(Path.GetTempPath(), "RoslynMcpIlBuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Graph_from_read_values_equals_the_graph_built_by_reading_the_same_files_again()
    {
        var neighbors = IlNeighborMapBuilder.Build(_shop.Inputs);
        IlAssemblyReadResult[] reads = [.. _shop.Inputs.Select(input => IlAssemblyReader.ReadFile(input.OutputFilePath))];

        var fromValues = IlDependencyGraphBuilder.Build(reads, neighbors);
        var fromFiles = IlDependencyGraphBuilder.BuildFromFiles(_shop.Inputs, neighbors);

        var description = IlGraphDescription.Describe(fromValues);
        Assert.Equal(IlGraphDescription.Describe(fromFiles), description);
        Assert.Equal(IlGraphDescription.Describe(_shop.Result), description);
        Assert.Contains(description, line => line.StartsWith("reach DiscoveryWithLoads Shop.Tests.ModuleLoaderTests", StringComparison.Ordinal));
    }

    [Fact]
    public void Graph_is_built_from_read_values_after_their_files_are_gone()
    {
        var lib = Copy(_shop.LibPath, "lib");
        var tests = Copy(_shop.TestsPath, "tests");
        Copy(_shop.LibPath, "tests");
        IlAssemblyInput[] inputs = [new(lib, IsTestProject: false), new(tests, IsTestProject: true)];
        var neighbors = IlNeighborMapBuilder.Build(inputs);
        var fromFiles = IlDependencyGraphBuilder.BuildFromFiles(inputs, neighbors);
        IlAssemblyReadResult[] reads = [.. inputs.Select(input => IlAssemblyReader.ReadFile(input.OutputFilePath))];

        File.Delete(lib);
        File.Delete(tests);
        var fromValues = IlDependencyGraphBuilder.Build(reads, neighbors);

        Assert.Empty(fromValues.Unread);
        Assert.Equal(IlGraphDescription.Describe(fromFiles), IlGraphDescription.Describe(fromValues));
        Assert.True(fromValues.Closures.Reaches(
            IlShopFixture.Test("ModuleLoaderTests"),
            IlShopFixture.Lib("UnrelatedService"),
            IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Replacing_one_read_value_rebuilds_the_graph_without_reading_the_other_files()
    {
        // The use of a caller that keeps the values: only the changed assembly is read again.
        var lib = Copy(_shop.LibPath, "lib");
        var plugins = Copy(_shop.PluginsPath, "plugins");
        IlAssemblyReadResult[] reads = [IlAssemblyReader.ReadFile(lib), IlAssemblyReader.ReadFile(plugins)];
        File.Delete(lib);
        File.Copy(_shop.OtherPath, plugins, overwrite: true);

        var rebuilt = IlDependencyGraphBuilder.Build([reads[0], IlAssemblyReader.ReadFile(plugins)], IlNeighborMap.Empty);

        Assert.Empty(rebuilt.Unread);
        Assert.Equal(
            new[] { IlShopFixture.LibAssembly, IlShopFixture.OtherAssembly },
            rebuilt.Graph.Assemblies.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Failed_read_values_are_returned_as_unread_and_the_graph_is_built_from_the_rest()
    {
        var missing = Path.Combine(_directory, "Absent.dll");
        IlAssemblyReadResult[] reads = [IlAssemblyReader.ReadFile(missing), IlAssemblyReader.ReadFile(_shop.PluginsPath)];

        var result = IlDependencyGraphBuilder.Build(reads, IlNeighborMap.Empty);

        var unread = Assert.Single(result.Unread);
        Assert.Equal(missing, unread.Path);
        Assert.Equal(IlAssemblyFailureKind.Missing, unread.Reason);
        Assert.Equal([IlShopFixture.PluginsAssembly], result.Graph.Assemblies);
    }

    [Fact]
    public void Assemblies_with_one_name_are_merged_into_one_assembly_and_one_node_per_type()
    {
        // One project loaded for two target frameworks: two output files with the same assembly name.
        var first = IlFixtureCompiler.Compile("Multi", IlFixtureSources.MultiFirstFramework, Path.Combine(_directory, "net8.0"));
        var second = IlFixtureCompiler.Compile("Multi", IlFixtureSources.MultiSecondFramework, Path.Combine(_directory, "net10.0"));
        IlAssemblyInput[] inputs = [new(first, IsTestProject: false), new(second, IsTestProject: false)];
        var user = new IlTypeName("Multi", "Multi.User");

        var result = IlDependencyGraphBuilder.BuildFromFiles(inputs, IlNeighborMapBuilder.Build(inputs));

        Assert.Empty(result.Unread);
        Assert.Equal(["Multi"], result.Graph.Assemblies);
        Assert.Single(result.Graph.Types, type => type == user);
        Assert.Contains(new IlTypeName("Multi", "Multi.OnlyInSecond"), result.Graph.Types);

        // A dependency in any of the builds counts.
        var dependencies = result.DependenciesOf(user);
        Assert.Contains(new IlTypeName("Multi", "Multi.FirstTarget"), dependencies);
        Assert.Contains(new IlTypeName("Multi", "Multi.SecondTarget"), dependencies);
    }

    [Fact]
    public void Neighbors_of_every_build_of_a_test_project_are_united()
    {
        // The test project is loaded for two target frameworks; each output directory has its own copies.
        var lib = Copy(_shop.LibPath, "lib");
        var plugins = Copy(_shop.PluginsPath, "plugins");
        var firstTests = Copy(_shop.TestsPath, Path.Combine("tests", "net8.0"));
        var secondTests = Copy(_shop.TestsPath, Path.Combine("tests", "net10.0"));
        Copy(_shop.LibPath, Path.Combine("tests", "net8.0"));
        Copy(_shop.PluginsPath, Path.Combine("tests", "net10.0"));
        IlAssemblyInput[] inputs =
        [
            new(lib, IsTestProject: false),
            new(plugins, IsTestProject: false),
            new(firstTests, IsTestProject: true),
            new(secondTests, IsTestProject: true),
        ];

        var result = IlDependencyGraphBuilder.BuildFromFiles(inputs, IlNeighborMapBuilder.Build(inputs));

        var test = IlShopFixture.Test("ModuleLoaderTests");
        Assert.Single(result.Closures.TestClasses, type => type == test);
        Assert.True(result.Closures.Reaches(test, IlShopFixture.Lib("UnrelatedService"), IlDependencyStage.DiscoveryWithLoads));
        Assert.True(result.Closures.Reaches(
            test,
            new IlTypeName(IlShopFixture.PluginsAssembly, "Shop.Plugins.ReportPlugin"),
            IlDependencyStage.DiscoveryWithLoads));
        result.AssertEveryStageIncludesThePrevious();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_class_concrete_in_one_target_framework_is_kept_when_another_framework_makes_it_abstract(bool abstractFirst)
    {
        const string abstractSource = """
            namespace MultiTests;

            public abstract class Fixture
            {
                [Xunit.Fact]
                public void Run() => System.GC.KeepAlive(typeof(Shop.OrderConsumer));
            }
            """;
        var concreteSource = abstractSource.Replace("public abstract class", "public sealed class", StringComparison.Ordinal);
        var abstractPath = IlFixtureCompiler.Compile("MultiTests", abstractSource, Path.Combine(_directory, "abstract"), _shop.LibPath);
        var concretePath = IlFixtureCompiler.Compile("MultiTests", concreteSource, Path.Combine(_directory, "concrete"), _shop.LibPath);
        var abstractRead = IlAssemblyReader.ReadFile(abstractPath);
        var concreteRead = IlAssemblyReader.ReadFile(concretePath);
        var libraryRead = IlAssemblyReader.ReadFile(_shop.LibPath);
        var test = new IlTypeName("MultiTests", "MultiTests.Fixture");
        var target = IlShopFixture.Lib("OrderConsumer");

        Assert.Empty(IlDependencyGraphBuilder.Build([abstractRead], IlNeighborMap.Empty).Closures.TestClasses);
        Assert.Equal([test], IlDependencyGraphBuilder.Build([concreteRead], IlNeighborMap.Empty).Closures.TestClasses);
        IlAssemblyReadResult[] reads = abstractFirst
            ? [libraryRead, abstractRead, concreteRead]
            : [libraryRead, concreteRead, abstractRead];
        var result = IlDependencyGraphBuilder.Build(reads, IlNeighborMap.Empty);

        Assert.Empty(result.Unread);
        Assert.Equal([test], result.Closures.TestClasses);
        Assert.All(Enum.GetValues<IlDependencyStage>(), stage =>
        {
            Assert.True(result.Closures.Reaches(test, target, stage));
            Assert.Equal([test], result.Closures.AffectedTestClasses([target], stage));
        });
        result.AssertEveryStageIncludesThePrevious();
    }

    private string Copy(string source, string subdirectory)
    {
        var directory = Path.Combine(_directory, subdirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(source));
        File.Copy(source, path);
        return path;
    }

    private readonly IlShopFixture _shop;
    private readonly string _directory;
}
