using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

/// <summary>The planner's filter over whole test classes, used by the stages of narrowing.</summary>
public sealed class VstestClassFilterTests
{
    [Fact]
    public void Each_class_gets_one_term_that_runs_all_of_its_tests_even_when_the_inventory_is_incomplete()
    {
        var filter = VstestRunPlanner.PlanClasses(
            [Class("Ns.First"), Class("Ns.Outer+Inner", complete: false), Class("Ns.Name(with)&chars")],
            CancellationToken.None);

        Assert.Null(filter.Whole);
        Assert.Equal(
            string.Join(
                '|',
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.First."),
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.Outer+Inner."),
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.Name(with)&chars.")),
            filter.Filter);
    }

    [Fact]
    public void No_classes_give_neither_a_filter_nor_a_reason()
    {
        var filter = VstestRunPlanner.PlanClasses([], CancellationToken.None);

        Assert.Null(filter.Filter);
        Assert.Null(filter.Whole);
    }

    [Theory]
    [InlineData("Unknown or unmeasured closed generic runtime identity; run the whole project.")]
    [InlineData(null)]
    public void Class_without_a_runtime_name_requires_the_whole_project(string? reason)
    {
        var filter = VstestRunPlanner.PlanClasses(
            [Class("Ns.First"), Class(null) with { UnresolvedReason = reason }],
            CancellationToken.None);

        Assert.Null(filter.Filter);
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, filter.Whole?.Kind);
        Assert.Equal(reason ?? "Concrete adapter/runtime identity is unknown or ambiguous; run the whole project.", filter.Whole?.Message);
    }

    [Fact]
    public void Fixture_with_arguments_requires_the_whole_project()
    {
        var fixture = Class(null) with
        {
            UnresolvedReason = "Runtime fixture constructor/source arguments require the whole project.",
            HasFixtureArguments = true,
        };

        var filter = VstestRunPlanner.PlanClasses([fixture], CancellationToken.None);

        Assert.Null(filter.Filter);
        Assert.Equal(TestNarrowingWholeKind.FixtureArguments, filter.Whole?.Kind);
        Assert.Equal("Runtime fixture constructor/source arguments require the whole project.", filter.Whole?.Message);
    }

    [Fact]
    public void Filter_over_the_budget_requires_the_whole_project()
    {
        VstestTestClass[] classes =
        [
            .. Enumerable.Range(0, 400).Select(index => Class($"Ns.ClassWithALongNameThatFillsTheFilterBudget{index:000}")),
        ];

        var filter = VstestRunPlanner.PlanClasses(classes, CancellationToken.None);

        Assert.Null(filter.Filter);
        Assert.Equal(TestNarrowingWholeKind.FilterOverBudget, filter.Whole?.Kind);
        Assert.Equal(
            $"Escaped VSTest filter exceeds the {VstestRunPlanner.MaxFilterLength}-character budget; run the whole project.",
            filter.Whole?.Message);
    }

    private static VstestTestClass Class(string? runtimeName, bool complete = true) => new(
        _project,
        [new BaselineMemberKey(SyntaxKind.MethodDeclaration, runtimeName ?? "Ns.Unknown`0", "Check", 0, "", "")],
        runtimeName,
        complete,
        null,
        false);

    private static readonly ProjectId _project = ProjectId.CreateNewId();
}
