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
