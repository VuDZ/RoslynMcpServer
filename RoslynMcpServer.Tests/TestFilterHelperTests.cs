using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class TestFilterHelperTests
{
    [Fact]
    public async Task BuildFilter_methodOnly_shortName_usesLeadingDotContains()
    {
        var (filter, _) = await TestFilterHelper.BuildFilterAsync(
            solution: null,
            className: null,
            methodName: "GetAppStorePrices_GetPricesReport_AllDataCorrect",
            CancellationToken.None);

        Assert.Equal(
            "FullyQualifiedName~.GetAppStorePrices_GetPricesReport_AllDataCorrect",
            filter);
        Assert.DoesNotContain("=", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("()", filter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildFilter_methodOnly_dottedFqn_doesNotPrefixExtraDot()
    {
        const string fqn =
            "InAppPurchasing.IntegrationTests.BackofficeTests.GetPricesTests.GetAppStorePricesTests.GetAppStorePrices_GetPricesReport_AllDataCorrect";

        var (filter, _) = await TestFilterHelper.BuildFilterAsync(
            solution: null,
            className: null,
            methodName: fqn,
            CancellationToken.None);

        Assert.Equal($"FullyQualifiedName~{fqn}", filter);
        Assert.False(filter.StartsWith("FullyQualifiedName~.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildFilter_classAndMethod_simpleNames_usesLeadingDotSuffix()
    {
        var (filter, _) = await TestFilterHelper.BuildFilterAsync(
            solution: null,
            className: "GetAppStorePricesTests",
            methodName: "GetAppStorePrices_GetPricesReport_AllDataCorrect",
            CancellationToken.None);

        Assert.Equal(
            "FullyQualifiedName~.GetAppStorePricesTests.GetAppStorePrices_GetPricesReport_AllDataCorrect",
            filter);
        Assert.DoesNotContain("=", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("()", filter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildFilter_classAndMethod_dottedClass_noExtraLeadingDot()
    {
        var (filter, _) = await TestFilterHelper.BuildFilterAsync(
            solution: null,
            className: "InAppPurchasing.IntegrationTests.GetAppStorePricesTests",
            methodName: "GetAppStorePrices_GetPricesReport_AllDataCorrect",
            CancellationToken.None);

        Assert.Equal(
            "FullyQualifiedName~InAppPurchasing.IntegrationTests.GetAppStorePricesTests.GetAppStorePrices_GetPricesReport_AllDataCorrect",
            filter);
    }

    [Fact]
    public async Task BuildFilter_classOnly_containsWithoutLeadingDotRequirement()
    {
        var (filter, _) = await TestFilterHelper.BuildFilterAsync(
            solution: null,
            className: "GetAppStorePricesTests",
            methodName: null,
            CancellationToken.None);

        Assert.Equal("FullyQualifiedName~GetAppStorePricesTests", filter);
    }

    [Fact]
    public async Task BuildFilter_resolves_method_marked_with_custom_fact_derived_attribute()
    {
        // Custom attributes derived from a framework root must resolve: get_test_list and the
        // filter now share one matcher (TestAttributeMatcher), so a class using
        // AnalyzerLifecycleFactAttribute-style attributes is filterable too.
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Lifecycle.Tests", LanguageNames.CSharp)
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddMetadataReference(MetadataReference.CreateFromFile(
                Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));
        var document = project.AddDocument(
            "LifecycleTests.cs",
            SourceText.From("""
                using Xunit;

                namespace Analyzer.Lifecycle.Tests;

                internal sealed class CustomFactAttribute : FactAttribute {}

                public class LifecycleTests
                {
                    [CustomFact] public void DerivedOne() {}
                }
                """),
            filePath: Path.Combine(Path.GetTempPath(), "LifecycleTests.cs"));
        workspace.TryApplyChanges(document.Project.Solution);

        // A method name resolves only when IsTestMethod accepts the (derived) attribute.
        var (filter, description) = await TestFilterHelper.BuildFilterAsync(
            workspace.CurrentSolution,
            className: "LifecycleTests",
            methodName: "DerivedOne",
            CancellationToken.None);

        Assert.Equal("FullyQualifiedName~Analyzer.Lifecycle.Tests.LifecycleTests.DerivedOne", filter);
        Assert.Contains("Roslyn-resolved FQN", description, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeFilterValue_escapesVstestSpecials()
    {
        var escaped = TestFilterHelper.EscapeFilterValue("Ns.Class.Method(arg)&x|y=z!~\\");

        Assert.Equal(@"Ns.Class.Method\(arg\)\&x\|y\=z\!\~\\", escaped);
    }

    [Fact]
    public void BuildSimpleOrDottedContainsNeedle_rules()
    {
        Assert.Equal(".Short", TestFilterHelper.BuildSimpleOrDottedContainsNeedle("Short"));
        Assert.Equal("Ns.Class.Method", TestFilterHelper.BuildSimpleOrDottedContainsNeedle("Ns.Class.Method"));
        Assert.Equal("Ns.Class.Method", TestFilterHelper.BuildSimpleOrDottedContainsNeedle(".Ns.Class.Method"));
    }

    [Fact]
    public void BuildClassMethodContainsNeedle_usesSimpleMethodSegment()
    {
        Assert.Equal(
            ".FooTests.Bar",
            TestFilterHelper.BuildClassMethodContainsNeedle("FooTests", "Bar"));
        Assert.Equal(
            "Ns.FooTests.Bar",
            TestFilterHelper.BuildClassMethodContainsNeedle("Ns.FooTests", "Ns.FooTests.Bar"));
    }
}
