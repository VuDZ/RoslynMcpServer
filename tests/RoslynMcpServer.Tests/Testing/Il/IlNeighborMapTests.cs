using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// "Next to the test assembly" for a load by name: the solution assemblies a copy of which lies in the output
/// directory of the test project. <c>Lib</c> and <c>Tests</c> are built into separate directories, and
/// <c>LoaderTests</c> calls <c>Assembly.Load</c> without naming anything of <c>Lib</c> in code.
/// </summary>
public sealed class IlNeighborMapTests : IClassFixture<IlNeighborFixture>
{
    public IlNeighborMapTests(IlNeighborFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Copy_of_the_library_next_to_the_test_assembly_makes_the_loading_test_depend_on_its_types()
    {
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.LibPath, layout.TestsDirectory);

        var result = layout.BuildGraph();

        Assert.Empty(result.Unread);
        Assert.False(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.References));
        Assert.False(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.Discovery));
        Assert.True(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.DiscoveryWithLoads));
        Assert.Equal(
            [IlNeighborLayout.LoaderTests],
            result.Closures.AffectedTestClasses([IlNeighborLayout.LibType], IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Without_a_copy_next_to_the_test_assembly_the_loading_test_does_not_depend_on_the_library()
    {
        // The output directory of Lib itself is another directory and is not what the test process loads from.
        var layout = _fixture.NewLayout();

        var result = layout.BuildGraph();

        Assert.Contains(IlNeighborLayout.LibType, result.Graph.Types);
        Assert.False(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.DiscoveryWithLoads));
        Assert.Empty(result.Closures.AffectedTestClasses([IlNeighborLayout.LibType], IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Assembly_outside_the_solution_next_to_the_test_assembly_is_not_in_the_answer()
    {
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.PackageSource, layout.TestsDirectory);

        var map = layout.BuildMap();
        var result = layout.BuildGraph();

        Assert.Empty(map.NeighborsByTestOutputPath[layout.TestsPath]);
        Assert.DoesNotContain(IlNeighborLayout.PackageAssembly, result.Graph.Assemblies);
        Assert.DoesNotContain(
            result.Closures.ReachedTypes(IlNeighborLayout.LoaderTests, IlDependencyStage.DiscoveryWithLoads),
            type => type.Assembly != IlNeighborLayout.TestsAssembly);
    }

    [Fact]
    public void Directory_of_a_third_project_holding_a_copy_of_the_test_assembly_does_not_widen_the_answer()
    {
        // Other references the test project, so a build copies Tests.dll next to Other.dll. That makes Other
        // a neighbor of the copy, not of the test assembly the test process starts from.
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.TestsPath, layout.OtherDirectory);

        var map = layout.BuildMap();
        var result = layout.BuildGraph();

        Assert.Empty(map.NeighborsByTestOutputPath[layout.TestsPath]);
        Assert.Contains(IlNeighborLayout.OtherType, result.Graph.Types);
        Assert.False(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.OtherType, IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Copy_in_a_subdirectory_of_the_test_output_is_not_a_neighbor()
    {
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.LibPath, Path.Combine(layout.TestsDirectory, "plugins"));

        var result = layout.BuildGraph();

        Assert.Empty(layout.BuildMap().NeighborsByTestOutputPath[layout.TestsPath]);
        Assert.False(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Map_names_a_neighbor_by_the_output_path_of_its_own_project_and_has_an_entry_per_test_project()
    {
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.LibPath, layout.TestsDirectory);
        IlNeighborLayout.Place(layout.OtherPath, layout.TestsDirectory);
        IlNeighborLayout.Place(layout.PackageSource, layout.TestsDirectory);

        var map = layout.BuildMap();

        var entry = Assert.Single(map.NeighborsByTestOutputPath);
        Assert.Equal(layout.TestsPath, entry.Key);
        Assert.Equal(
            new[] { layout.LibPath, layout.OtherPath }.Order(StringComparer.Ordinal),
            entry.Value.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Projects_built_into_one_directory_are_neighbors_of_every_test_project_there()
    {
        // A solution with a common output directory: every assembly lies next to every test assembly.
        var layout = _fixture.NewLayout();
        var lib = IlNeighborLayout.Place(layout.LibPath, layout.TestsDirectory);
        var other = IlNeighborLayout.Place(layout.OtherPath, layout.TestsDirectory);
        IlAssemblyInput[] inputs =
        [
            new(lib, IsTestProject: false),
            new(other, IsTestProject: true),
            new(layout.TestsPath, IsTestProject: true),
        ];

        var map = IlNeighborMapBuilder.Build(inputs);

        Assert.Equal(new[] { lib, other }.Order(StringComparer.Ordinal), map.NeighborsByTestOutputPath[layout.TestsPath]);
        Assert.Equal(new[] { lib, layout.TestsPath }.Order(StringComparer.Ordinal), map.NeighborsByTestOutputPath[other]);
        Assert.Equal(2, map.NeighborsByTestOutputPath.Count);
    }

    [Fact]
    public void Copies_next_to_the_test_assembly_are_not_read()
    {
        // The code of a neighbor comes from the output assembly of its project. The copy only has to be there:
        // here it is not even an assembly.
        var layout = _fixture.NewLayout();
        File.WriteAllText(Path.Combine(layout.TestsDirectory, Path.GetFileName(layout.LibPath)), "not an assembly");

        var result = layout.BuildGraph();

        Assert.Empty(result.Unread);
        Assert.True(result.Closures.Reaches(IlNeighborLayout.LoaderTests, IlNeighborLayout.LibType, IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Test_project_whose_output_directory_is_missing_has_no_neighbors()
    {
        var layout = _fixture.NewLayout();
        var missing = Path.Combine(layout.TestsDirectory, "missing", "Tests.dll");
        IlAssemblyInput[] inputs = [new(layout.LibPath, IsTestProject: false), new(missing, IsTestProject: true)];

        var map = IlNeighborMapBuilder.Build(inputs);

        Assert.Empty(Assert.Single(map.NeighborsByTestOutputPath).Value);
    }

    [Fact]
    public void Test_without_a_load_keeps_its_answer_whatever_lies_next_to_the_test_assembly()
    {
        var layout = _fixture.NewLayout();
        IlNeighborLayout.Place(layout.LibPath, layout.TestsDirectory);

        var result = layout.BuildGraph();

        Assert.Equal(
            result.Closures.ReachedTypes(IlNeighborLayout.PlainTests, IlDependencyStage.Discovery),
            result.Closures.ReachedTypes(IlNeighborLayout.PlainTests, IlDependencyStage.DiscoveryWithLoads));
        Assert.False(result.Closures.Reaches(IlNeighborLayout.PlainTests, IlNeighborLayout.LibType, IlDependencyStage.DiscoveryWithLoads));
    }

    [Fact]
    public void Every_stage_includes_the_previous_one_in_every_layout()
    {
        var bare = _fixture.NewLayout();
        var withLibrary = _fixture.NewLayout();
        IlNeighborLayout.Place(withLibrary.LibPath, withLibrary.TestsDirectory);
        var withPackage = _fixture.NewLayout();
        IlNeighborLayout.Place(withPackage.PackageSource, withPackage.TestsDirectory);
        var copiedToOther = _fixture.NewLayout();
        IlNeighborLayout.Place(copiedToOther.TestsPath, copiedToOther.OtherDirectory);

        Assert.All(
            [bare, withLibrary, withPackage, copiedToOther],
            layout => layout.BuildGraph().AssertEveryStageIncludesThePrevious());
    }

    private readonly IlNeighborFixture _fixture;
}
