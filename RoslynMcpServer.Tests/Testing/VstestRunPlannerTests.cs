using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class VstestRunPlannerTests
{
    public static IEnumerable<object[]> AdapterProbes()
    {
        foreach (var adapter in new[] { "xunit-v2", "xunit-v3", "nunit", "mstest" })
        {
            foreach (var probe in new[] { "nested", "generic", "inherited", "parameters" })
            {
                yield return [adapter, probe];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AdapterProbes))]
    public void Saved_adapter_names_select_every_measured_case(string adapterName, string probe)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine(RepositoryRoot.Find(),
            "docs", "rapid-tia", "epoch-1-project-level", "evidence", "task-06-adapter-names", "catalog.json")));
        var adapter = catalog.RootElement.GetProperty("Adapters").EnumerateArray()
            .Single(item => item.GetProperty("Adapter").GetString() == adapterName);
        var measured = adapter.GetProperty("Filters").EnumerateArray()
            .Single(item => item.GetProperty("Probe").GetString() == probe);
        var expectedFilter = measured.GetProperty("Filter").GetString()!;
        var needle = expectedFilter["FullyQualifiedName~".Length..];
        var methodName = probe switch
        {
            "nested" => "Nested",
            "generic" => "Generic",
            "inherited" => "Inherited",
            _ => "Rows"
        };
        var sourceOwner = probe switch
        {
            "nested" => "Source.Outer`0.Inner`0",
            "generic" => "Source.GenericTests`1",
            "inherited" => "Source.InheritedBase`0",
            _ => "Source.ParameterTests`0"
        };
        var project = ProjectId.CreateNewId();
        var key = Key(methodName, sourceOwner);
        var runtimeClass = needle[..needle.LastIndexOf('.')];
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], runtimeClass, false, null, false)));

        Assert.Equal(expectedFilter, plan.Filter);
        Assert.Contains(plan.Reasons, reason => reason.Contains("superset", StringComparison.Ordinal));
        var actualIds = adapter.GetProperty("Cases").EnumerateArray()
            .Where(item => item.GetProperty("FullyQualifiedName").GetString()!.Contains(needle, StringComparison.Ordinal))
            .Select(item => item.GetProperty("Id").GetString()).Order().ToArray();
        var expectedIds = measured.GetProperty("Results").EnumerateArray()
            .Select(item => item.GetProperty("TestCaseId").GetString()).Order().ToArray();
        Assert.Equal(expectedIds, actualIds);
        Assert.Equal(probe == "parameters" ? 2 : 1, actualIds.Length);
        Assert.Contains("Changed test.", plan.Reasons);
    }

    [Fact]
    public void Contains_filters_include_CheckExtra_and_union_names_and_reasons()
    {
        var project = ProjectId.CreateNewId();
        var check = Key("Check");
        var extra = Key("CheckExtra");
        var other = Key("Other", "Ns.Other`0");
        var result = Impact(Test(project, check), new AffectedTest(project, check, ["Another change."]), Test(project, other));
        var plan = Assert.Single(Plan(result,
            new VstestTestClass(project, [check, extra], "Ns.Cases", true, null, false),
            new VstestTestClass(project, [other], "Ns.Other", false, null, false)));

        Assert.Equal("FullyQualifiedName~Ns.Cases.Check|FullyQualifiedName~Ns.Other.Other", plan.Filter);
        Assert.Equal(["Ns.Cases.Check", "Ns.Cases.CheckExtra"],
            new[] { "Ns.Cases.Check", "Ns.Cases.CheckExtra", "Ns.Cases.Unrelated" }
                .Where(name => name.Contains("Ns.Cases.Check", StringComparison.Ordinal)));
        Assert.Contains("Another change.", plan.Reasons);
        Assert.Equal(1, plan.Reasons.Count(reason => reason == "Changed test."));
        Assert.Contains(plan.Reasons, reason => reason.Contains("superset", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Class_collapse_requires_a_complete_current_inventory(bool isComplete)
    {
        var project = ProjectId.CreateNewId();
        var first = Key("First");
        var second = Key("Second");
        var plan = Assert.Single(Plan(Impact(Test(project, first), Test(project, second)),
            new VstestTestClass(project, [first, second], "Ns.Cases", isComplete, null, false)));

        Assert.Equal(isComplete ? "FullyQualifiedName~Ns.Cases."
            : "FullyQualifiedName~Ns.Cases.First|FullyQualifiedName~Ns.Cases.Second", plan.Filter);
    }

    [Fact]
    public void Every_known_concrete_owner_of_an_inherited_source_method_is_selected()
    {
        var project = ProjectId.CreateNewId();
        var key = Key("Inherited", "Ns.Base`0");
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], "Ns.FirstDerived", false, null, false),
            new VstestTestClass(project, [key], "Ns.SecondDerived", false, null, false)));

        Assert.Equal("FullyQualifiedName~Ns.FirstDerived.Inherited|FullyQualifiedName~Ns.SecondDerived.Inherited", plan.Filter);
    }

    [Theory]
    [InlineData("Unknown closed generic runtime identity.")]
    [InlineData("Unknown inherited concrete runtime identity.")]
    [InlineData("Ambiguous adapter runtime identity.")]
    public void Unresolved_runtime_identity_runs_the_whole_project_with_the_supplied_reason(string reason)
    {
        var project = ProjectId.CreateNewId();
        var key = Key("Check");
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], null, true, reason, false)));

        Assert.Null(plan.Filter);
        Assert.Contains(reason, plan.Reasons);
        Assert.Contains("Changed test.", plan.Reasons);
    }

    [Fact]
    public void A_missing_selected_identity_is_never_silently_omitted()
    {
        var project = ProjectId.CreateNewId();
        var known = Key("Known");
        var missing = Key("Missing");
        var plan = Assert.Single(Plan(Impact(Test(project, known), Test(project, missing)),
            new VstestTestClass(project, [known], "Ns.Cases", true, null, false)));

        Assert.Null(plan.Filter);
        Assert.Contains(plan.Reasons, reason => reason.Contains("runtime identity", StringComparison.OrdinalIgnoreCase)
            && reason.Contains("Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_known_name_with_an_ambiguity_reason_cannot_hide_an_unresolved_mapping()
    {
        var project = ProjectId.CreateNewId();
        var key = Key("Check");
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], "Ns.Cases", true, "Ambiguous concrete owners.", false)));

        Assert.Null(plan.Filter);
        Assert.Contains("Ambiguous concrete owners.", plan.Reasons);
    }

    [Fact]
    public void Projects_with_the_same_source_key_keep_their_own_runtime_mapping()
    {
        var firstProject = ProjectId.CreateNewId();
        var secondProject = ProjectId.CreateNewId();
        var key = Key("Check");
        var plans = Plan(Impact(Test(firstProject, key), Test(secondProject, key)),
            new VstestTestClass(firstProject, [key], "Ns.First", false, null, false),
            new VstestTestClass(secondProject, [key], null, false, "Unknown second project identity.", false));

        Assert.Equal(2, plans.Count);
        Assert.Equal("FullyQualifiedName~Ns.First.Check", plans.Single(plan => plan.ProjectId == firstProject).Filter);
        var second = plans.Single(plan => plan.ProjectId == secondProject);
        Assert.Null(second.Filter);
        Assert.Contains("Unknown second project identity.", second.Reasons);
    }

    [Fact]
    public void Project_expansion_dominates_selected_tests_without_requiring_runtime_inventory()
    {
        var project = ProjectId.CreateNewId();
        var expandedOnly = ProjectId.CreateNewId();
        var result = Impact(Test(project, Key("Check"))) with
        {
            ExpandedTestProjects = new Dictionary<ProjectId, IReadOnlyList<string>>
            {
                [project] = ["Production change."],
                [expandedOnly] = ["Transitive project expansion."]
            }
        };
        var plans = Plan(result);

        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Null(plan.Filter));
        Assert.Contains("Production change.", plans.Single(plan => plan.ProjectId == project).Reasons);
        Assert.Contains("Transitive project expansion.", plans.Single(plan => plan.ProjectId == expandedOnly).Reasons);
        Assert.DoesNotContain(plans.SelectMany(plan => plan.Reasons),
            reason => reason.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Ns.ArgFixture", "M", "Ns.ArgFixture(\"a\").M", "Ns.ArgFixture(\"b\").M")]
    [InlineData("Ns.SourceFixture", "S", "Ns.SourceFixture(1).S", "Ns.SourceFixture(1).S")]
    [InlineData("Ns.GenericFixture<Int32>", "M", "Ns.GenericFixture<Int32>(\"a\").M", "Ns.GenericFixture<Int32>(\"b\").M")]
    public void Fixture_constructor_or_source_arguments_force_a_safe_whole_project_plan(
        string runtimeClass, string method, string firstCase, string secondCase)
    {
        var project = ProjectId.CreateNewId();
        var key = Key(method);
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], runtimeClass, true, null, true)));

        Assert.Null(plan.Filter);
        Assert.Contains(plan.Reasons, reason => reason.Contains("fixture", StringComparison.OrdinalIgnoreCase)
            && reason.Contains("arguments", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(runtimeClass + "(", firstCase);
        Assert.StartsWith(runtimeClass + "(", secondCase);
        Assert.DoesNotContain(runtimeClass + ".", firstCase, StringComparison.Ordinal);
        Assert.DoesNotContain(runtimeClass + ".", secondCase, StringComparison.Ordinal);
    }

    [Fact]
    public void NUnit_generic_type_arguments_alone_do_not_mean_fixture_constructor_arguments()
    {
        var project = ProjectId.CreateNewId();
        var key = Key("Generic", "AdapterNames.NUnit.GenericTests`1");
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], "AdapterNames.NUnit.GenericTests<Int32>", true, null, false)));

        Assert.Equal("FullyQualifiedName~AdapterNames.NUnit.GenericTests<Int32>.", plan.Filter);
        Assert.Contains("AdapterNames.NUnit.GenericTests<Int32>.", "AdapterNames.NUnit.GenericTests<Int32>.Generic");
    }

    [Fact]
    public void Filter_values_reuse_the_existing_escape_rules()
    {
        var project = ProjectId.CreateNewId();
        var key = Key("Check(arg)&x|y=z!~\\");
        const string runtimeClass = "Ns.Outer+Inner";
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], runtimeClass, false, null, false)));

        Assert.Equal("FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue(runtimeClass + "." + key.Name), plan.Filter);
        Assert.Equal("FullyQualifiedName~Ns.Outer+Inner.Check\\(arg\\)\\&x\\|y\\=z\\!\\~\\\\", plan.Filter);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Filter_budget_is_checked_after_escaping_at_the_exact_boundary(int overflow)
    {
        const string prefix = "FullyQualifiedName~";
        var padding = VstestRunPlanner.MaxFilterLength - prefix.Length - "Ns.".Length - ".Check".Length - 2 + overflow;
        var runtimeClass = "Ns." + new string('a', padding) + "&";
        var project = ProjectId.CreateNewId();
        var key = Key("Check");
        var expected = prefix + TestFilterHelper.EscapeFilterValue(runtimeClass + ".Check");
        var plan = Assert.Single(Plan(Impact(Test(project, key)),
            new VstestTestClass(project, [key], runtimeClass, false, null, false)));

        Assert.Equal(VstestRunPlanner.MaxFilterLength + overflow, expected.Length);
        if (overflow > 0)
        {
            Assert.Null(plan.Filter);
            Assert.Contains(plan.Reasons, reason => reason.Contains("budget", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Equal(expected, plan.Filter);
        }
    }

    [Fact]
    public void Class_collapse_happens_before_applying_the_filter_budget()
    {
        var project = ProjectId.CreateNewId();
        var keys = Enumerable.Range(0, 1000).Select(index => Key("Check" + index)).ToArray();
        var plan = Assert.Single(Plan(Impact(keys.Select(key => Test(project, key)).ToArray()),
            new VstestTestClass(project, keys, "Ns.Cases", true, null, false)));

        Assert.Equal("FullyQualifiedName~Ns.Cases.", plan.Filter);
    }

    [Fact]
    public void Empty_selection_does_not_schedule_inventory_only_projects()
    {
        var project = ProjectId.CreateNewId();
        Assert.Empty(Plan(Impact(), new VstestTestClass(project, [Key("Check")], "Ns.Cases", true, null, false)));
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => VstestRunPlanner.Plan(Impact(), [], cancellation.Token));
    }

    private static IReadOnlyList<VstestProjectRunPlan> Plan(TestImpactResult impact, params VstestTestClass[] classes) =>
        VstestRunPlanner.Plan(impact, classes, CancellationToken.None);

    private static TestImpactResult Impact(params AffectedTest[] tests) => new(tests,
        new Dictionary<ProjectId, IReadOnlyList<string>>(), [], false, false);

    private static AffectedTest Test(ProjectId project, BaselineMemberKey key) => new(project, key, ["Changed test."]);

    private static BaselineMemberKey Key(string name, string owner = "Ns.Cases`0") =>
        new(SyntaxKind.MethodDeclaration, owner, name, 0, "", "");
}
