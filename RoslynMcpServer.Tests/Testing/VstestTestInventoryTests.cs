using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class VstestTestInventoryTests
{
    [Fact]
    public async Task Nested_types_use_runtime_plus_and_partial_inventory_includes_every_part()
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace,
            "namespace Ns { public partial class Outer { public partial class Inner { [Fact] public void First() {} } } }",
            "namespace Ns { public partial class Outer { public partial class Inner { [Fact] public void Second() {} } } }");
        var testClass = Assert.Single(await VstestTestInventory.BuildAsync(solution, CancellationToken.None));

        Assert.Equal("Ns.Outer+Inner", testClass.RuntimeName);
        Assert.True(testClass.IsComplete);
        Assert.Null(testClass.UnresolvedReason);
        Assert.Equal(["First", "Second"], testClass.Tests.Select(key => key.Name).Order().ToArray());
    }

    [Fact]
    public async Task Inherited_tests_belong_to_every_concrete_owner_and_include_own_tests()
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace, """
            namespace Ns {
                public abstract class Base { [Fact] public void Inherited() {} }
                public class First : Base { [Fact] public void Own() {} }
                public class Second : Base {}
            }
            """);
        var classes = await VstestTestInventory.BuildAsync(solution, CancellationToken.None);

        Assert.Equal(["Ns.First", "Ns.Second"], classes.Select(testClass => testClass.RuntimeName!).Order().ToArray());
        Assert.All(classes, testClass => Assert.True(testClass.IsComplete));
        Assert.Equal(["Inherited", "Own"], classes.Single(testClass => testClass.RuntimeName == "Ns.First")
            .Tests.Select(key => key.Name).Order().ToArray());
        Assert.All(classes, testClass => Assert.Contains(testClass.Tests, key =>
            key.ContainingTypePath == "Ns.Base`0" && key.Name == "Inherited"));
    }

    [Fact]
    public async Task Closed_generic_inheritance_uses_the_concrete_class_and_open_identity_is_unresolved()
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace, """
            namespace Ns {
                public abstract class Base<T> { [Fact] public void Generic() {} }
                public class Closed : Base<int> {}
                public class Open<T> { [Fact] public void Unknown() {} }
            }
            """);
        var classes = await VstestTestInventory.BuildAsync(solution, CancellationToken.None);

        var closed = Assert.Single(classes, testClass => testClass.RuntimeName == "Ns.Closed");
        Assert.Equal("Generic", Assert.Single(closed.Tests).Name);
        Assert.Null(closed.UnresolvedReason);
        var open = Assert.Single(classes, testClass => testClass.Tests.Any(key => key.Name == "Unknown"));
        Assert.Null(open.RuntimeName);
        Assert.NotNull(open.UnresolvedReason);
    }

    [Theory]
    [InlineData("[NUnit.Framework.TestFixture(typeof(int))]", "Ns.Generic<Int32>", false, false)]
    [InlineData("[NUnit.Framework.TestFixture(typeof(string))]", null, false, true)]
    [InlineData("[NUnit.Framework.TestFixture(typeof(int), 1)]", null, true, true)]
    [InlineData("[NUnit.Framework.TestFixtureSource(\"Cases\")]", null, true, true)]
    public async Task NUnit_generic_fixture_only_uses_measured_closed_form(
        string attributes, string? runtimeName, bool fixtureArguments, bool unresolved)
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace,
            "namespace Ns { " + attributes + " public class Generic<T> { [NUnit.Framework.Test] public void Check() {} } }");
        var testClass = Assert.Single(await VstestTestInventory.BuildAsync(solution, CancellationToken.None));

        Assert.Equal(runtimeName, testClass.RuntimeName);
        Assert.Equal(fixtureArguments, testClass.HasFixtureArguments);
        Assert.Equal(unresolved, testClass.UnresolvedReason is not null);
    }

    [Theory]
    [InlineData("[NUnit.Framework.TestFixture(\"a\")]", true)]
    [InlineData("[NUnit.Framework.TestFixtureSource(\"Cases\")]", true)]
    [InlineData("[NUnit.Framework.TestFixture]", false)]
    [InlineData("", false)]
    public async Task Fixture_constructor_metadata_is_distinct_from_ordinary_CSharp_parameters(string attributes, bool expected)
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace,
            "namespace Ns { " + attributes + " public class Fixture { public Fixture(string value) {} [NUnit.Framework.Test] public void Check() {} } }");
        var testClass = Assert.Single(await VstestTestInventory.BuildAsync(solution, CancellationToken.None));

        Assert.Equal(expected, testClass.HasFixtureArguments);
    }

    [Theory]
    [InlineData("[NUnit.Framework.TestFixture(42)]")]
    [InlineData("[NUnit.Framework.TestFixtureSource(\"Cases\")]")]
    public async Task Inherited_NUnit_fixture_arguments_are_not_mistaken_for_a_plain_concrete_class(string attribute)
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace, $$"""
            namespace Ns {
                {{attribute}}
                public abstract class Base {
                    public static object[] Cases => new object[] { new object[] { 42 } };
                    protected Base(int value) {}
                }
                public class Derived : Base {
                    public Derived(int value) : base(value) {}
                    [NUnit.Framework.Test] public void Own() {}
                }
            }
            """);
        var testClass = Assert.Single(await VstestTestInventory.BuildAsync(solution, CancellationToken.None));

        Assert.True(testClass.HasFixtureArguments);
        Assert.Null(testClass.RuntimeName);
        Assert.NotNull(testClass.UnresolvedReason);
    }

    [Fact]
    public async Task Source_base_in_another_project_is_included_in_the_concrete_class_inventory()
    {
        using var workspace = new AdhocWorkspace();
        var solution = CreateSolution(workspace,
            "namespace Ns { public abstract class Base { [Fact] public void Inherited() {} } }");
        var baseProject = solution.ProjectIds.Single();
        var derivedProject = ProjectId.CreateNewId();
        solution = solution.AddProject(derivedProject, "DerivedSpecs", "DerivedSpecs", LanguageNames.CSharp)
            .AddMetadataReference(derivedProject, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddProjectReference(derivedProject, new ProjectReference(baseProject))
            .AddDocument(DocumentId.CreateNewId(derivedProject), "Derived.cs",
                SourceText.From("namespace Ns { public class Derived : Base { [Fact] public void Own() {} } }"),
                filePath: Path.Combine(Path.GetTempPath(), "Derived.cs"));
        var testClass = Assert.Single(await VstestTestInventory.BuildAsync(solution, CancellationToken.None));

        Assert.Equal(derivedProject, testClass.ProjectId);
        Assert.Equal("Ns.Derived", testClass.RuntimeName);
        Assert.True(testClass.IsComplete);
        Assert.Equal(["Inherited", "Own"], testClass.Tests.Select(key => key.Name).Order().ToArray());
    }

    [Fact]
    public async Task Cancellation_does_not_publish_a_partial_inventory()
    {
        using var workspace = new AdhocWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VstestTestInventory.BuildAsync(CreateSolution(workspace, "class C {}"), cancellation.Token));
    }

    private static Solution CreateSolution(AdhocWorkspace workspace, params string[] sources)
    {
        var project = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(project, "Specs", "Specs", LanguageNames.CSharp)
            .AddMetadataReference(project, MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        foreach (var source in sources.Prepend("""
            public class FactAttribute : System.Attribute {}
            namespace NUnit.Framework {
                public class TestAttribute : System.Attribute {}
                public class TestFixtureAttribute : System.Attribute { public TestFixtureAttribute(params object[] values) {} }
                public class TestFixtureSourceAttribute : System.Attribute { public TestFixtureSourceAttribute(string name) {} }
            }
            """))
        {
            var id = DocumentId.CreateNewId(project);
            solution = solution.AddDocument(id, id.Id + ".cs", SourceText.From(source), filePath: Path.Combine(Path.GetTempPath(), id.Id + ".cs"));
        }
        return solution;
    }
}
