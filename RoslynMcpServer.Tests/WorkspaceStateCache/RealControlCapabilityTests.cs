using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class RealControlCapabilityTests
{
    [Theory]
    [InlineData("emit", "excluded-emit-signing")]
    [InlineData("metadata-documentation", "excluded-metadata-documentation")]
    [InlineData("selected-symbols", "signing-state-unknown")]
    public void Excluded_operations_refuse_when_no_ordinary_route_is_available(string operation, string expectedReason)
    {
        var log = new List<string>();
        Assert.Equal(expectedReason, CapabilityOperationGate.Execute(CSharpCompilation.Create("RefusalControl"),
            operation, false, () => Assert.Fail("A refused operation executed."), null, log.Add));
        Assert.Contains("route=refusal", Assert.Single(log));
        Assert.Contains("reason=" + expectedReason, log[0]);
    }

    [Theory]
    [InlineData("System.Reflection.AssemblyKeyFile(\"company.snk\")", "signing-dependent")]
    [InlineData("signed-friend", "signing-dependent")]
    [InlineData("MissingSigningAttribute", "signing-state-unknown")]
    public void Assembly_attributes_are_checked_before_the_selected_query(string attribute, string expectedReason)
    {
        if (attribute == "signed-friend")
        {
            var publicKey = Convert.ToHexString(typeof(object).Assembly.GetName().GetPublicKey()!);
            attribute = "System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend, PublicKey=" + publicKey + "\")";
        }
        var compilation = CSharpCompilation.Create("AttributeControl",
            [CSharpSyntaxTree.ParseText("[assembly: " + attribute + "]")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var ordinaryCalls = 0;
        Assert.Equal(expectedReason, CapabilityOperationGate.Execute(compilation, "selected-symbols", true,
            () => Assert.Fail("An excluded attribute reached hydrated execution."), () => ordinaryCalls++, _ => { }));
        Assert.Equal(1, ordinaryCalls);
    }

    [Theory]
    [InlineData("selected-symbols", false, false, "signing-state-unknown")]
    [InlineData("selected-symbols", true, true, "signing-dependent")]
    [InlineData("emit", true, false, "excluded-emit-signing")]
    [InlineData("metadata-documentation", true, false, "excluded-metadata-documentation")]
    [InlineData("documentation-analyzer", true, false, "excluded-metadata-documentation")]
    [InlineData("arbitrary-analyzer", true, false, "signing-state-unknown")]
    public void Excluded_operations_route_before_hydrated_execution(
        string operation, bool known, bool signing, string expectedReason)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (signing)
        {
            options = options.WithCryptoKeyFile("company.snk");
        }
        var compilation = CSharpCompilation.Create("ExcludedControl", options: options);
        var events = new List<string>();
        var result = CapabilityOperationGate.Execute(compilation, operation, known,
            () => events.Add("hydrated-side-effect"), () => events.Add("ordinary-started"), events.Add);
        Assert.Equal(expectedReason, result);
        Assert.Equal(2, events.Count);
        Assert.Contains("reason=" + expectedReason, events[0]);
        Assert.Contains("project=ExcludedControl", events[0]);
        Assert.Contains("route=ordinary-load", events[0]);
        Assert.Equal("ordinary-started", events[1]);
        Assert.DoesNotContain("hydrated-side-effect", events);
    }
}
