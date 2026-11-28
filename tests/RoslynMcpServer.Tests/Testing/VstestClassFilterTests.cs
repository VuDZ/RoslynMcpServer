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

        var expression = Assert.IsType<VstestClassFilterExpression>(filter);
        Assert.Equal(
            string.Join(
                '|',
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.First."),
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.Outer+Inner."),
                "FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue("Ns.Name(with)&chars.")),
            expression.Filter);
    }

    [Fact]
    public void NoClassesGiveAnEmptySelection()
    {
        Assert.IsType<VstestClassFilterEmpty>(VstestRunPlanner.PlanClasses([], CancellationToken.None));
    }

    [Theory]
    [InlineData("Unknown or unmeasured closed generic runtime identity; run the whole project.")]
    [InlineData(null)]
    public void Class_without_a_runtime_name_requires_the_whole_project(string? reason)
    {
        var filter = VstestRunPlanner.PlanClasses(
            [Class("Ns.First"), Class(null) with { UnresolvedReason = reason }],
            CancellationToken.None);

        var whole = Assert.IsType<VstestClassFilterWholeProject>(filter).Reason;
        Assert.Equal(TestNarrowingWholeKind.UnresolvedTestClass, whole.Kind);
        Assert.Equal(
            reason ?? "Concrete adapter/runtime identity is unknown or ambiguous; run the whole project.",
            whole.Message);
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

        var whole = Assert.IsType<VstestClassFilterWholeProject>(filter).Reason;
        Assert.Equal(TestNarrowingWholeKind.FixtureArguments, whole.Kind);
        Assert.Equal("Runtime fixture constructor/source arguments require the whole project.", whole.Message);
    }

    [Fact]
    public void Filter_over_the_budget_requires_the_whole_project()
    {
        VstestTestClass[] classes =
        [
            .. Enumerable.Range(0, 400).Select(index => Class($"Ns.ClassWithALongNameThatFillsTheFilterBudget{index:000}")),
        ];

        var filter = VstestRunPlanner.PlanClasses(classes, CancellationToken.None);

        var whole = Assert.IsType<VstestClassFilterWholeProject>(filter).Reason;
        Assert.Equal(TestNarrowingWholeKind.FilterOverBudget, whole.Kind);
        Assert.Equal(
            $"Escaped VSTest filter exceeds the {VstestRunPlanner.MaxFilterLength}-character budget; run the whole project.",
            whole.Message);
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
