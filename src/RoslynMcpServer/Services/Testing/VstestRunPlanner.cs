using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing;

/// <summary>Builds a conservative VSTest plan without discovery, compilation, or execution.</summary>
internal static class VstestRunPlanner
{
    // Leave room for the executable, project path, and other arguments below Windows' 32,767-character limit.
    internal const int MaxFilterLength = 16000;

    internal static IReadOnlyList<VstestProjectRunPlan> Plan(
        TestImpactResult impact,
        IReadOnlyList<VstestTestClass> currentClasses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(currentClasses);
        cancellationToken.ThrowIfCancellationRequested();

        var plans = new List<VstestProjectRunPlan>();
        var projects = impact.ExpandedTestProjects.Keys.Concat(impact.Tests.Select(test => test.ProjectId)).Distinct();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tests = impact.Tests.Where(test => test.ProjectId == project).ToArray();
            var reasons = tests.SelectMany(test => test.Reasons).ToHashSet(StringComparer.Ordinal);
            if (impact.ExpandedTestProjects.TryGetValue(project, out var expansionReasons))
            {
                reasons.UnionWith(expansionReasons.Select(reason => reason.Text));
                reasons.Add("Project-level expansion selects the whole project.");
                plans.Add(new VstestProjectRunPlan(project, null, reasons.ToArray()));
                continue;
            }

            var selected = tests.Select(test => test.Key).ToHashSet();
            var classes = currentClasses.Where(testClass => testClass.ProjectId == project
                && testClass.Tests.Any(selected.Contains)).ToArray();
            var unresolved = UnresolvedReasons(selected, classes, cancellationToken);
            if (unresolved.Count > 0)
            {
                reasons.UnionWith(unresolved);
                plans.Add(new VstestProjectRunPlan(project, null, reasons.ToArray()));
                continue;
            }

            var filter = BuildFilter(selected, classes, cancellationToken);
            if (filter.Length > MaxFilterLength)
            {
                reasons.Add(_overBudgetReason);
                plans.Add(new VstestProjectRunPlan(project, null, reasons.ToArray()));
                continue;
            }
            reasons.Add("VSTest contains filters select a superset; names containing a selected name also run.");
            plans.Add(new VstestProjectRunPlan(project, filter, reasons.ToArray()));
        }
        return plans;
    }

    /// <summary>
    /// Plans a filter that runs every test of each class. An empty selection needs no filter.
    /// As in <see cref="Plan"/>, a class without a runtime name, a fixture with arguments and a filter over
    /// <see cref="MaxFilterLength"/> require the whole project.
    /// </summary>
    /// <remarks>
    /// A class term is <c>Runtime.Name.</c> whether or not the inventory of the class is complete: a stage selects
    /// the class as a whole, and the term runs every test the adapter reports under it.
    /// </remarks>
    internal static VstestClassFilter PlanClasses(IReadOnlyList<VstestTestClass> classes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classes);
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testClass in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subject = testClass.RuntimeName ?? testClass.TopLevelTypeName;
            if (testClass.HasFixtureArguments)
            {
                return Whole(TestNarrowingWholeKind.FixtureArguments, subject, FixtureArgumentsReason);
            }

            if (testClass.UnresolvedReason is not null || string.IsNullOrWhiteSpace(testClass.RuntimeName))
            {
                var reason = string.IsNullOrWhiteSpace(testClass.UnresolvedReason) ? UnknownIdentityReason : testClass.UnresolvedReason;
                return Whole(TestNarrowingWholeKind.UnresolvedTestClass, subject, reason);
            }

            terms.Add("FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue(testClass.RuntimeName + "."));
        }

        if (terms.Count == 0)
        {
            return new VstestClassFilterEmpty();
        }

        var filter = string.Join('|', terms);
        return filter.Length > MaxFilterLength
            ? Whole(TestNarrowingWholeKind.FilterOverBudget, null, _overBudgetReason)
            : new VstestClassFilterExpression(filter);

        static VstestClassFilterWholeProject Whole(TestNarrowingWholeKind kind, string? subject, string message) =>
            new(new TestNarrowingWholeReason(kind, subject, message));
    }

    private const string UnknownIdentityReason = "Concrete adapter/runtime identity is unknown or ambiguous; run the whole project.";

    private const string FixtureArgumentsReason = "Runtime fixture constructor/source arguments require the whole project.";

    private static IReadOnlyList<string> UnresolvedReasons(
        IReadOnlySet<BaselineMemberKey> selected,
        IReadOnlyList<VstestTestClass> classes,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        foreach (var key in selected.Except(classes.SelectMany(testClass => testClass.Tests)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reasons.Add("Unknown runtime identity for selected test: " + key.ContainingTypePath + "." + key.Name);
        }
        foreach (var testClass in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (testClass.UnresolvedReason is not null || string.IsNullOrWhiteSpace(testClass.RuntimeName))
            {
                reasons.Add(string.IsNullOrWhiteSpace(testClass.UnresolvedReason)
                    ? UnknownIdentityReason
                    : testClass.UnresolvedReason);
            }
            if (testClass.HasFixtureArguments)
            {
                // T06-C1 (docs/rapid-tia/epoch-1-project-level/task-07-run-plan-med.md): arguments break Class.Method and Class. filters.
                reasons.Add(FixtureArgumentsReason);
            }
        }
        return reasons;
    }

    private static string BuildFilter(
        IReadOnlySet<BaselineMemberKey> selected,
        IReadOnlyList<VstestTestClass> classes,
        CancellationToken cancellationToken)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testClass in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (testClass.IsComplete && testClass.Tests.All(selected.Contains))
            {
                terms.Add("FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue(testClass.RuntimeName + "."));
                continue;
            }
            foreach (var key in testClass.Tests.Where(selected.Contains))
            {
                cancellationToken.ThrowIfCancellationRequested();
                terms.Add("FullyQualifiedName~" + TestFilterHelper.EscapeFilterValue(testClass.RuntimeName + "." + key.Name));
            }
        }
        return string.Join('|', terms);
    }

    private static readonly string _overBudgetReason =
        $"Escaped VSTest filter exceeds the {MaxFilterLength}-character budget; run the whole project.";
}
