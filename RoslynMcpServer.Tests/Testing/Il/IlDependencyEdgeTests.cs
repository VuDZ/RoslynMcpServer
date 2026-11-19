using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>What gives an edge between two top-level types and what does not.</summary>
[Collection(IlShopCollection.Name)]
public sealed class IlDependencyEdgeTests
{
    public IlDependencyEdgeTests(IlShopFixture shop)
    {
        _shop = shop;
    }

    [Fact]
    public void Base_type_and_interface_are_dependencies()
    {
        var dependencies = DependenciesOf("SupertypeUser");

        Assert.Contains(IlShopFixture.Edge("BaseTarget"), dependencies);
        Assert.Contains(IlShopFixture.Edge("IInterfaceTarget"), dependencies);
    }

    [Fact]
    public void Types_of_a_field_and_of_a_method_signature_are_dependencies()
    {
        var dependencies = DependenciesOf("SignatureUser");

        Assert.Contains(IlShopFixture.Edge("FieldTarget"), dependencies);
        Assert.Contains(IlShopFixture.Edge("ParameterTarget"), dependencies);
        Assert.Contains(IlShopFixture.Edge("ReturnTarget"), dependencies);
    }

    [Fact]
    public void Attribute_and_its_typeof_argument_are_dependencies()
    {
        var dependencies = DependenciesOf("AttributeUser");

        Assert.Contains(IlShopFixture.Edge("MarkerAttribute"), dependencies);
        Assert.Contains(IlShopFixture.Edge("AttributeArgumentTarget"), dependencies);
    }

    [Fact]
    public void Type_of_a_local_variable_is_a_dependency()
    {
        Assert.Contains(IlShopFixture.Edge("LocalTarget"), DependenciesOf("LocalUser"));
    }

    [Theory]
    [InlineData("TypeTokenUser", "TypeTokenTarget")]
    [InlineData("MethodTokenUser", "MethodTokenTarget")]
    [InlineData("FieldTokenUser", "FieldTokenTarget")]
    public void Type_method_and_field_tokens_in_a_method_body_are_dependencies(string user, string target)
    {
        Assert.Contains(IlShopFixture.Edge(target), DependenciesOf(user));
    }

    [Fact]
    public void Lambda_and_async_state_machine_bodies_belong_to_the_declaring_type()
    {
        var dependencies = DependenciesOf("LambdaUser");

        Assert.Contains(IlShopFixture.Edge("LambdaTarget"), dependencies);
        Assert.Contains(IlShopFixture.Edge("AsyncTarget"), dependencies);
    }

    [Fact]
    public void Code_of_a_nested_type_belongs_to_the_outermost_type()
    {
        Assert.Contains(IlShopFixture.Edge("NestedTarget"), DependenciesOf("NestedOwner"));

        // Nested types are not nodes: neither the declared one nor the closure class of a lambda nor the
        // state machine of an async method.
        Assert.DoesNotContain(
            _shop.Result.Graph.Types,
            type => type.Name.Contains('+') || type.Name.Contains("Inner") || type.Name.Contains("<>c") || type.Name.Contains("d__"));
    }

    [Fact]
    public void Reference_after_the_switch_instruction_is_read()
    {
        // The case is about the variable-length operand of the IL switch instruction, so the compiled method
        // must have one: opcode 0x45 followed by the number of targets.
        var body = IlFixtureInspector.MethodBody(_shop.LibPath, "Shop.Edges.SwitchUser", "Pick");
        Assert.True(body.AsSpan().IndexOf(new byte[] { 0x45, 4, 0, 0, 0 }) >= 0, "The fixture method has no IL switch instruction.");

        Assert.Contains(IlShopFixture.Edge("SwitchTarget"), DependenciesOf("SwitchUser"));
        Assert.Equal(0, _shop.Result.Graph.UnreadableBodies);
    }

    [Fact]
    public void Type_named_in_a_switch_by_type_is_a_dependency()
    {
        Assert.Contains(IlShopFixture.Edge("TypeSwitchTarget"), DependenciesOf("TypeSwitchUser"));
    }

    [Fact]
    public void Generic_constraint_and_catch_clause_are_dependencies()
    {
        Assert.Contains(IlShopFixture.Edge("ConstraintTarget"), DependenciesOf("ConstraintUser`1"));
        Assert.Contains(IlShopFixture.Edge("CatchTarget"), DependenciesOf("CatchUser"));
    }

    /// <summary>
    /// A known limit of reading IL: the compiler copies a constant into its user and replaces
    /// <c>nameof</c> with a string, so no reference remains.
    /// </summary>
    [Fact]
    public void Constant_and_nameof_leave_no_dependency()
    {
        Assert.DoesNotContain(IlShopFixture.Edge("ConstantHolder"), DependenciesOf("ConstantUser"));
        Assert.DoesNotContain(IlShopFixture.Edge("NameofTarget"), DependenciesOf("NameofUser"));
    }

    [Fact]
    public void Consumer_depends_on_the_contract_and_not_on_the_implementation()
    {
        var dependencies = _shop.Result.DependenciesOf(IlShopFixture.Lib("OrderConsumer"));

        Assert.Contains(IlShopFixture.Lib("IOrderService"), dependencies);
        Assert.DoesNotContain(IlShopFixture.Lib("OrderService"), dependencies);
    }

    [Fact]
    public void Generic_registration_names_the_implementation()
    {
        var dependencies = _shop.Result.DependenciesOf(IlShopFixture.Lib("OrderRegistration"));

        Assert.Contains(IlShopFixture.Lib("OrderService"), dependencies);
        Assert.Contains(IlShopFixture.Lib("IOrderService"), dependencies);
    }

    [Fact]
    public void Type_never_depends_on_itself()
    {
        var graph = _shop.Result.Graph;

        Assert.All(Enumerable.Range(0, graph.Types.Length), node => Assert.DoesNotContain(node, graph.Dependencies[node]));
    }

    [Fact]
    public void Assembly_facts_name_the_assembly_by_its_manifest_and_list_top_level_types_only()
    {
        var read = IlAssemblyReader.ReadFile(_shop.LibPath);

        Assert.Null(read.Failure);
        Assert.Equal(_shop.LibPath, read.Path);
        Assert.Equal(IlShopFixture.LibAssembly, read.Facts!.Name);
        Assert.Contains(read.Facts.Types, type => type.Name == IlShopFixture.Edge("NestedOwner"));
        Assert.DoesNotContain(read.Facts.Types, type => type.Name.Name.Contains("Inner", StringComparison.Ordinal));
        Assert.All(read.Facts.Types, type => Assert.Equal(IlShopFixture.LibAssembly, type.Name.Assembly));
    }

    private IReadOnlyList<IlTypeName> DependenciesOf(string user) => _shop.Result.DependenciesOf(IlShopFixture.Edge(user));

    private readonly IlShopFixture _shop;
}
