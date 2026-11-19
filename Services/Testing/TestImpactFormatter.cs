using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing;

internal static class TestImpactFormatter
{
    internal const string Warning = "Approximate result (rapid TIA): not a substitute for the full test suite or CI.";

    internal static string Format(
        Solution solution,
        TestImpactEstimate estimate,
        TestNarrowingResult narrowing,
        TestClassStageLevel depth,
        bool includeDetails,
        IReadOnlyDictionary<ProjectId, TestImpactProjectRun>? runs,
        string state)
    {
        var changes = estimate.Documents.SelectMany(document => document.Changes is null
            ? (document.Current?.Members ?? []).Select(member => (document.Path, member.Key))
            : document.Changes.Where(change => change.Key is not null)
                .Select(change => (document.Path, Key: change.Key!))).Distinct().ToArray();
        var analyses = runs is null ? [narrowing] : runs.Values.Select(result => result.Analysis).OfType<TestNarrowingResult>().ToArray();
        var byProject = analyses.SelectMany(result => result.Projects).ToDictionary(project => project.ProjectId);
        var changedTypes = byProject.Values.SelectMany(project => project.ChangedTypes)
            .Concat(analyses.SelectMany(result => result.DirectChangedTypes)).Distinct().ToArray();
        var body = new StringBuilder();
        body.AppendLine($"Changed since load: {changes.Length} members in {estimate.ChangedPaths.Count} files. "
            + $"{changedTypes.Length} changed type{(changedTypes.Length == 1 ? "" : "s")} identified in "
            + (runs is null ? "compared assemblies. " : "assemblies compared after the builds of this run. ")
            + $"Directly selected: {estimate.Impact.Tests.Count} tests. Run plan: {estimate.Plans.Count} projects.");
        foreach (var plan in estimate.Plans.OrderBy(plan => solution.GetProject(plan.ProjectId)?.FilePath, StringComparer.Ordinal))
        {
            var project = solution.GetProject(plan.ProjectId)!;
            AppendProject(body, project.FilePath ?? project.Name, plan, byProject.GetValueOrDefault(plan.ProjectId),
                depth, runs?.GetValueOrDefault(plan.ProjectId)?.Execution, includeDetails);
        }
        if (runs is null)
        {
            AppendNotCovered(body, estimate.NotCovered);
        }
        if (estimate.CompilationErrors.Count > 0)
        {
            body.AppendLine("Compilation errors in changed documents:");
            foreach (var error in estimate.CompilationErrors)
            {
                body.AppendLine("- " + error);
            }
        }
        if (includeDetails)
        {
            body.AppendLine("Changed members:");
            foreach (var change in changes)
            {
                body.AppendLine($"- {change.Path}: {change.Key.ContainingTypePath}.{change.Key.Name}");
            }
            body.AppendLine("Changed types:");
            foreach (var changedType in changedTypes.OrderBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.Assembly, StringComparer.Ordinal))
            {
                body.AppendLine("- " + changedType);
            }
            AppendSelectedTests(body, solution, estimate, byProject);
        }
        if (runs is { Count: 0 })
        {
            body.AppendLine("No test processes selected.");
        }
        body.AppendLine(state);
        if (runs is not null)
        {
            AppendNotCovered(body, estimate.NotCovered);
        }
        return body.ToString().TrimEnd();
    }

    private static void AppendProject(
        StringBuilder body,
        string project,
        VstestProjectRunPlan plan,
        TestProjectNarrowing? narrowing,
        TestClassStageLevel depth,
        DotNetTestResult? run,
        bool includeDetails)
    {
        var stage = narrowing?.SelectedStage(depth);
        var filter = narrowing is null ? plan.Filter : stage?.Filter;
        if (narrowing is not null && stage is { SelectsTests: false })
        {
            body.AppendLine($"No tests: `{project}`. {EmptyStageReason(narrowing, depth)}");
        }
        else
        {
            body.Append("Run: `").Append(project).Append('`');
            body.AppendLine(filter is null ? " whole project." : " with filter `" + filter + "`");
            var whole = narrowing?.Whole ?? stage?.Whole;
            if (whole is not null || stage is null)
            {
                var reasons = whole is null ? plan.Reasons : plan.Reasons.Prepend(WholeMessage(whole, afterBuild: run is not null));
                body.AppendLine("Reasons: " + string.Join("; ", reasons.Distinct(StringComparer.Ordinal)));
            }
        }
        if (narrowing is not null && stage is not null)
        {
            AppendStages(body, narrowing.Stages, includeDetails);
        }
        if (run is not null)
        {
            AppendRun(body, run, filter);
        }
    }

    /// <remarks>
    /// The hint for an output older than the edit depends on who built last. Before a run it is the caller's
    /// build that is missing; after the run's own build, repeating that advice would loop, since the likely cause
    /// is a source saved during the build.
    /// </remarks>
    private static string WholeMessage(TestNarrowingWholeReason whole, bool afterBuild) => whole.Kind switch
    {
        TestNarrowingWholeKind.NonCSharpInput when whole.Subject is { } path => "Non-C# input changed: " + path,
        TestNarrowingWholeKind.NotRebuilt or TestNarrowingWholeKind.DependentNotRebuilt or TestNarrowingWholeKind.ReferenceAssemblyMissing =>
            whole.Message + (afterBuild
                ? "; this run has just built the project, so a source likely changed during the build; call again."
                : "; build and call again."),
        _ => whole.Message,
    };

    private static string EmptyStageReason(TestProjectNarrowing project, TestClassStageLevel depth)
    {
        if (project.Outcome == TestNarrowingOutcome.CodeUnchanged)
        {
            return "Compiled code did not change; no tests selected.";
        }
        if (project.Outcome == TestNarrowingOutcome.ChangedTypesWithoutTests)
        {
            const int PreviewLimit = 5;
            var count = project.ChangedTypes.Count;
            var types = count == 0 ? "" : ": " + string.Join("; ", project.ChangedTypes.Take(PreviewLimit));
            if (count > PreviewLimit)
            {
                types += "; … (use includeDetails for all changed types)";
            }
            return $"No test class reaches the changed compiled code. {count} changed type{(count == 1 ? "" : "s")}{types}. "
                + "The graph cannot see type names in strings or configuration.";
        }
        return $"No test classes at depth {TestImpactDepth.Name(depth)}. See the wider stages below.";
    }

    private static void AppendStages(StringBuilder body, IReadOnlyList<TestClassStage> stages, bool includeDetails)
    {
        var counts = stages.Select(stage => $"{TestImpactDepth.Name(stage.Level)}={stage.Classes.Count}"
            + (stage.Whole is null ? "" : " (whole project)"));
        body.AppendLine("Stages (classes): " + string.Join(", ", counts) + ".");
        if (includeDetails)
        {
            foreach (var stage in stages)
            {
                var classes = string.Join(", ", stage.Classes.Select(selection =>
                    selection.Class.RuntimeName ?? selection.Class.TopLevelTypeName ?? "unknown runtime name"));
                var scope = stage.Whole is null ? "" : ", whole project";
                body.AppendLine($"- {TestImpactDepth.Name(stage.Level)} ({stage.Classes.Count}{scope}): "
                    + (classes.Length == 0 ? "none" : classes));
            }
        }
    }

    private static void AppendSelectedTests(
        StringBuilder body,
        Solution solution,
        TestImpactEstimate estimate,
        IReadOnlyDictionary<ProjectId, TestProjectNarrowing> narrowing)
    {
        var plans = estimate.Plans.Where(plan => narrowing.GetValueOrDefault(plan.ProjectId)?.Stages.Count is null or 0).ToArray();
        if (plans.Length == 0)
        {
            return;
        }

        body.AppendLine("Selected tests:");
        var directProjects = plans.Select(plan => plan.ProjectId).ToHashSet();
        var wholeProjects = plans
            .Where(plan => plan.Filter is null || narrowing.GetValueOrDefault(plan.ProjectId)?.Whole is not null)
            .Select(plan => plan.ProjectId)
            .ToHashSet();
        var tests = estimate.Impact.Tests.Where(test => directProjects.Contains(test.ProjectId))
            .Select(test => (test.ProjectId, test.Key))
            .Concat(estimate.Classes.Where(testClass => wholeProjects.Contains(testClass.ProjectId))
                .SelectMany(testClass => testClass.Tests.Select(key => (testClass.ProjectId, Key: key)))).ToHashSet();
        foreach (var test in tests)
        {
            body.AppendLine($"- {solution.GetProject(test.ProjectId)?.Name}: {test.Key.ContainingTypePath}.{test.Key.Name}");
        }
        foreach (var testClass in estimate.Classes)
        {
            foreach (var key in testClass.Tests.Where(key => tests.Contains((testClass.ProjectId, key))))
            {
                body.AppendLine($"  Runtime: {solution.GetProject(testClass.ProjectId)?.Name}: "
                    + (testClass.RuntimeName is null ? "unknown (whole project)" : testClass.RuntimeName + "." + key.Name));
            }
        }
    }

    private static void AppendNotCovered(StringBuilder body, IReadOnlyList<string> limitations)
    {
        if (limitations.Count == 0)
        {
            body.AppendLine("Not covered: none.");
            return;
        }
        body.AppendLine("Not covered:");
        foreach (var limitation in limitations)
        {
            body.AppendLine("- " + limitation);
        }
    }

    private static void AppendRun(StringBuilder body, DotNetTestResult result, string? filter)
    {
        body.AppendLine("Outcome: " + result.OutcomeText);
        if (result.Parsed?.Summary is { } summary)
        {
            body.AppendLine($"Total: {summary.Total}; Passed: {summary.Passed}; Failed: {summary.Failed}; Skipped: {summary.Skipped}.");
        }
        if (result.Outcome == TestProcessOutcome.BuildFailed && result.BuildRun is { } build)
        {
            body.AppendLine(BuildOutputReport.Format(build.CombinedOutput, build.ExitCode, build.RunMetadata,
                includeBuildWarnings: false, subject: "Pre-test build", attachFullReport: false));
        }
        if (result.Outcome == TestProcessOutcome.Failed && result.TestRun is { } test)
        {
            var outputOptions = new TestOutputReportOptions(false, 0) { SuppressOutputOptionHints = true };
            body.AppendLine(VstestOutputParser.BuildMarkdownReport(result.Parsed!, test.ExitCode, test.CombinedOutput,
                filter, filterDescription: null, requireFilterMatch: false, outputOptions));
        }
        if (result.Outcome is TestProcessOutcome.TimedOut or TestProcessOutcome.Cancelled)
        {
            body.AppendLine(DotNetCliRunner.FormatHangHints(
                timedOut: result.Outcome == TestProcessOutcome.TimedOut,
                cancelled: result.Outcome == TestProcessOutcome.Cancelled));
        }
        if (result.Error is { } error)
        {
            body.AppendLine(error[..Math.Min(error.Length, 2500)]);
        }
    }
}
