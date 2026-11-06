using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Models;

namespace RoslynMcpServer.Services.Testing;

internal static class TestImpactFormatter
{
    internal const string Warning = "Approximate result (rapid TIA): not a substitute for the full test suite or CI.";

    internal static string Format(
        Solution solution,
        TestImpactEstimate estimate,
        bool includeDetails,
        IReadOnlyDictionary<ProjectId, DotNetTestResult>? runs = null,
        string? runState = null)
    {
        var changes = estimate.Documents.SelectMany(document => document.Changes is null
            ? (document.Current?.Members ?? []).Select(member => (document.Path, member.Key))
            : document.Changes.Where(change => change.Key is not null)
                .Select(change => (document.Path, Key: change.Key!))).Distinct().ToArray();
        var body = new StringBuilder();
        body.AppendLine($"Changed since load: {changes.Length} members in {estimate.ChangedPaths.Count} files. "
            + $"Directly selected: {estimate.Impact.Tests.Count} tests. Run plan: {estimate.Plans.Count} projects.");
        foreach (var plan in estimate.Plans.OrderBy(plan => solution.GetProject(plan.ProjectId)?.FilePath, StringComparer.Ordinal))
        {
            var project = solution.GetProject(plan.ProjectId)!;
            body.Append("Run: `").Append(project.FilePath ?? project.Name).Append('`');
            body.AppendLine(plan.Filter is null ? " whole project (project-level)." : " with filter `" + plan.Filter + "`");
            body.AppendLine("Reasons: " + string.Join("; ", plan.Reasons));
            if (runs is not null)
            {
                AppendRun(body, runs[plan.ProjectId], plan.Filter);
            }
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
            body.AppendLine("Selected tests:");
            var wholeProjects = estimate.Plans.Where(plan => plan.Filter is null).Select(plan => plan.ProjectId).ToHashSet();
            var tests = estimate.Impact.Tests.Select(test => (test.ProjectId, test.Key))
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
        if (runs is not null)
        {
            if (runs.Count == 0)
            {
                body.AppendLine("No test processes selected.");
            }
            body.AppendLine(runState ?? "State: project-level result only.");
            AppendNotCovered(body, estimate.NotCovered);
        }
        return body.ToString().TrimEnd();
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
