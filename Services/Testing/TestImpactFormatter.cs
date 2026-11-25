using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing;

internal static class TestImpactFormatter
{
    internal const string Warning = "Approximate result (rapid TIA): not a substitute for the full test suite or CI.";
    private const int MaxResponseBytes = 32 * 1024;
    private const int ExampleLimit = 3;

    internal static string Format(
        Solution solution,
        TestImpactEstimate estimate,
        TestNarrowingResult narrowing,
        TestClassStageLevel depth,
        bool includeDetails,
        IReadOnlyDictionary<ProjectId, TestImpactProjectRun>? runs,
        string state)
    {
        var changes = estimate.Documents.SelectMany(document => (document.Changes ?? [])
            .Where(change => change.Key is not null)
            .Select(change => (document.Path, Key: change.Key!))).Distinct().ToArray();
        var analyses = runs is null ? [narrowing] : runs.Values.Select(result => result.Analysis).OfType<TestNarrowingResult>().ToArray();
        var byProject = analyses.SelectMany(result => result.Projects).ToDictionary(project => project.ProjectId);
        var changedTypes = byProject.Values.SelectMany(project => project.ChangedTypes)
            .Concat(analyses.SelectMany(result => result.DirectChangedTypes)).Distinct().ToArray();
        var body = new Report();
        body.AppendLine(Warning, Priority.Essential);
        body.AppendLine($"Changed since load: {changes.Length} members in {ConfirmedChangedFileCount(estimate)} files. "
            + $"{changedTypes.Length} changed type{(changedTypes.Length == 1 ? "" : "s")} identified in "
            + (runs is null ? "compared assemblies. " : "assemblies compared after the builds of this run. ")
            + $"Directly selected: {estimate.Impact.Tests.Count} tests. Run plan: {estimate.Plans.Count} projects.",
            Priority.Essential);
        foreach (var plan in estimate.Plans.OrderBy(plan => solution.GetProject(plan.ProjectId)?.FilePath, StringComparer.Ordinal))
        {
            var project = solution.GetProject(plan.ProjectId)!;
            AppendProject(body, project.FilePath ?? project.Name, plan, byProject.GetValueOrDefault(plan.ProjectId),
                depth, runs?.GetValueOrDefault(plan.ProjectId)?.Execution, includeDetails);
        }
        if (runs is null)
        {
            AppendNotCovered(body, estimate.NotCovered, estimate.UnknownBaselinePaths);
        }
        if (estimate.CompilationErrors.Count > 0)
        {
            body.AppendLine("Compilation errors in changed documents:", Priority.Diagnostic);
            foreach (var error in estimate.CompilationErrors)
            {
                body.AppendLine("- " + error, Priority.Diagnostic);
            }
        }
        if (includeDetails)
        {
            body.AppendLine("Changed members:", Priority.Detail);
            foreach (var change in changes)
            {
                body.AppendLine($"- {change.Path}: {change.Key.ContainingTypePath}.{change.Key.Name}", Priority.Detail);
            }
            body.AppendLine("Changed types:", Priority.Detail);
            foreach (var changedType in changedTypes.OrderBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.Assembly, StringComparer.Ordinal))
            {
                body.AppendLine("- " + changedType, Priority.Detail);
            }
            AppendSelectedTests(body, solution, estimate, byProject);
        }
        if (runs is { Count: 0 })
        {
            body.AppendLine("No test processes selected.", Priority.Essential);
        }
        body.AppendLine(state, Priority.Essential);
        if (runs is not null)
        {
            AppendNotCovered(body, estimate.NotCovered, estimate.UnknownBaselinePaths);
        }
        return body.Format();
    }

    internal static string FormatEarlyResponse(string message, string? state, bool run)
    {
        var body = new Report();
        body.AppendLine(Warning, Priority.Essential);
        body.AppendLine(message);
        if (state is not null)
        {
            body.AppendLine(state, Priority.Essential);
        }
        if (run)
        {
            body.AppendLine("Not covered: no execution report is available for this request.", Priority.Essential);
        }
        return body.Format();
    }

    private static int ConfirmedChangedFileCount(TestImpactEstimate estimate)
    {
        var uncertainPaths = estimate.UnknownBaselinePaths.ToHashSet(InputPathCanon.Comparer);
        var confirmedPaths = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var document in estimate.Documents)
        {
            if (document.Changes is null)
            {
                uncertainPaths.Add(document.Path);
            }
            else if (document.Changes.Count > 0)
            {
                confirmedPaths.Add(document.Path);
            }
        }
        return estimate.ChangedPaths.Count(path => !uncertainPaths.Contains(path) || confirmedPaths.Contains(path));
    }

    private static void AppendProject(
        Report body,
        string project,
        VstestProjectRunPlan plan,
        TestProjectNarrowing? narrowing,
        TestClassStageLevel depth,
        DotNetTestResult? run,
        bool includeDetails)
    {
        var stage = narrowing?.SelectedStage(depth);
        var filter = narrowing is null ? plan.Filter : stage?.Filter;
        var outcome = FormatOutcome(run);
        var emptyStageReason = narrowing is not null && stage is { SelectsTests: false }
            ? EmptyStageReason(narrowing, depth)
            : null;
        string headline;
        string? compactHeadline = null;
        if (emptyStageReason is not null)
        {
            compactHeadline = $"No tests: `{project}`. Reason omitted from this response.";
            headline = $"No tests: `{project}`. {emptyStageReason}";
        }
        else if (filter is null)
        {
            headline = $"Run: `{project}` whole project.";
        }
        else
        {
            compactHeadline = $"Run: `{project}` with a filter omitted from this response.";
            headline = $"Run: `{project}` with filter `{filter}`";
        }
        var projectBlock = body.AppendLine(
            headline + outcome,
            Priority.Project,
            compactText: compactHeadline is null ? null : compactHeadline + outcome);
        if (emptyStageReason is null)
        {
            var whole = narrowing?.Whole ?? stage?.Whole;
            if (whole is not null || stage is null)
            {
                var reasons = GroupReasons(plan.Reasons);
                if (whole is not null)
                {
                    var wholeMessage = WholeMessage(whole, afterBuild: run is not null);
                    reasons = reasons.Where(reason => reason != wholeMessage).Prepend(wholeMessage);
                }
                body.AppendLine("Reasons: " + string.Join("; ", reasons), projectBlock: projectBlock);
            }
        }
        if (narrowing is not null && stage is not null)
        {
            AppendStages(body, projectBlock, narrowing.Stages, includeDetails);
        }
        if (run is not null)
        {
            AppendRunDiagnostics(body, projectBlock, run);
        }
    }

    private static IEnumerable<string> GroupReasons(IEnumerable<string> reasons)
    {
        var groups = reasons.Distinct(StringComparer.Ordinal).GroupBy(reason =>
        {
            var separator = reason.IndexOf(": ", StringComparison.Ordinal);
            return separator < 0 ? reason : reason[..(separator + 2)];
        }, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group.Count() <= ExampleLimit)
            {
                foreach (var reason in group)
                {
                    yield return reason;
                }
                continue;
            }

            var examples = group.Take(ExampleLimit).Select(reason => reason[group.Key.Length..]);
            yield return group.Key + $"{group.Count()} entries (examples: {string.Join(", ", examples)})";
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

    private static void AppendStages(
        Report body,
        int projectBlock,
        IReadOnlyList<TestClassStage> stages,
        bool includeDetails)
    {
        var counts = stages.Select(stage => $"{TestImpactDepth.Name(stage.Level)}={stage.Classes.Count}"
            + (stage.Whole is null ? "" : " (whole project)"));
        body.AppendLine("Stages (classes): " + string.Join(", ", counts) + ".", projectBlock: projectBlock);
        if (includeDetails)
        {
            foreach (var stage in stages)
            {
                var classes = string.Join(", ", stage.Classes.Select(selection =>
                    selection.Class.RuntimeName ?? selection.Class.TopLevelTypeName ?? "unknown runtime name"));
                var scope = stage.Whole is null ? "" : ", whole project";
                body.AppendLine($"- {TestImpactDepth.Name(stage.Level)} ({stage.Classes.Count}{scope}): "
                    + (classes.Length == 0 ? "none" : classes), Priority.Detail, projectBlock);
            }
        }
    }

    private static void AppendSelectedTests(
        Report body,
        Solution solution,
        TestImpactEstimate estimate,
        IReadOnlyDictionary<ProjectId, TestProjectNarrowing> narrowing)
    {
        var plans = estimate.Plans.Where(plan => narrowing.GetValueOrDefault(plan.ProjectId)?.Stages.Count is null or 0).ToArray();
        if (plans.Length == 0)
        {
            return;
        }

        body.AppendLine("Selected tests:", Priority.Detail);
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
            body.AppendLine($"- {solution.GetProject(test.ProjectId)?.Name}: {test.Key.ContainingTypePath}.{test.Key.Name}",
                Priority.Detail);
        }
        foreach (var testClass in estimate.Classes)
        {
            foreach (var key in testClass.Tests.Where(key => tests.Contains((testClass.ProjectId, key))))
            {
                body.AppendLine($"  Runtime: {solution.GetProject(testClass.ProjectId)?.Name}: "
                    + (testClass.RuntimeName is null ? "unknown (whole project)" : testClass.RuntimeName + "." + key.Name),
                    Priority.Detail);
            }
        }
    }

    private static void AppendNotCovered(
        Report body,
        IReadOnlyList<string> limitations,
        IReadOnlyCollection<string> unknownBaselinePaths)
    {
        if (limitations.Count == 0 && unknownBaselinePaths.Count == 0)
        {
            body.AppendLine("Not covered: none.", Priority.Essential);
            return;
        }
        body.AppendLine("Not covered:", Priority.Essential);
        if (unknownBaselinePaths.Count > 0)
        {
            var examples = string.Join(", ", unknownBaselinePaths.Order(InputPathCanon.Comparer).Take(ExampleLimit));
            var count = $"- Baseline unknown in {unknownBaselinePaths.Count} file(s)";
            body.AppendLine($"{count}: {examples}", Priority.Essential,
                compactText: count + ". Examples omitted from this response.");
        }
        foreach (var limitation in GroupReasons(limitations))
        {
            body.AppendLine("- " + limitation);
        }
    }

    private static string FormatOutcome(DotNetTestResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }
        var outcome = Environment.NewLine + "Outcome: " + result.OutcomeText;
        if (result.Parsed?.Summary is { } summary)
        {
            outcome += Environment.NewLine
                + $"Total: {summary.Total}; Passed: {summary.Passed}; Failed: {summary.Failed}; Skipped: {summary.Skipped}.";
        }
        return outcome;
    }

    private static void AppendRunDiagnostics(Report body, int projectBlock, DotNetTestResult result)
    {
        if (result.Outcome == TestProcessOutcome.BuildFailed && result.BuildRun is { } build)
        {
            var diagnostic = BuildOutputReport.Format(
                build.CombinedOutput,
                build.ExitCode,
                build.RunMetadata,
                includeBuildWarnings: false,
                subject: "Pre-test build",
                attachFullReport: false);
            body.AppendLine(diagnostic, Priority.Diagnostic, projectBlock);
        }
        if ((result.Outcome is TestProcessOutcome.Failed or TestProcessOutcome.Partial) && result.TestRun is { } test)
        {
            var outputOptions = new TestOutputReportOptions(false, 0) { SuppressOutputOptionHints = true };
            // The project headline owns the filter budget, so failure diagnostics do not repeat it.
            var diagnostic = VstestOutputParser.BuildMarkdownReport(
                result.Parsed!,
                test.ExitCode,
                test.CombinedOutput,
                filter: null,
                filterDescription: null,
                requireFilterMatch: false,
                outputOptions);
            body.AppendLine(diagnostic, Priority.Diagnostic, projectBlock);
        }
        if (result.Outcome is TestProcessOutcome.TimedOut or TestProcessOutcome.Cancelled)
        {
            body.AppendLine(DotNetCliRunner.FormatHangHints(
                timedOut: result.Outcome == TestProcessOutcome.TimedOut,
                cancelled: result.Outcome == TestProcessOutcome.Cancelled), Priority.Diagnostic, projectBlock);
        }
        if (result.Error is { } error)
        {
            body.AppendLine(error[..Math.Min(error.Length, 2500)], Priority.Diagnostic, projectBlock);
        }
    }

    private enum Priority
    {
        Essential,
        Project,
        Plan,
        Diagnostic,
        Detail,
    }

    private sealed class Report
    {
        internal int AppendLine(
            string text,
            Priority priority = Priority.Plan,
            int? projectBlock = null,
            string? compactText = null)
        {
            if (compactText is not null && Encoding.UTF8.GetByteCount(compactText) >= Encoding.UTF8.GetByteCount(text))
            {
                compactText = null;
            }
            var index = _blocks.Count;
            _blocks.Add((text, priority, projectBlock, compactText));
            return index;
        }

        internal string Format()
        {
            const int OmissionFooterReserve = 64;
            var remaining = MaxResponseBytes - OmissionFooterReserve;
            var kept = new string?[_blocks.Count];
            var omittedLines = 0;
            var indices = Enumerable.Range(0, _blocks.Count);
            // Account for every project summary and its diagnostics before spending bytes on long filters.
            foreach (var index in indices.Where(index => _blocks[index].Priority != Priority.Detail)
                .OrderBy(index => _blocks[index].Priority))
            {
                KeepBlock(index);
            }
            foreach (var index in indices)
            {
                var (text, _, _, compactText) = _blocks[index];
                if (compactText is not null && kept[index] is not null)
                {
                    var extra = Encoding.UTF8.GetByteCount(text) - Encoding.UTF8.GetByteCount(compactText);
                    if (extra <= remaining)
                    {
                        kept[index] = text;
                        remaining -= extra;
                    }
                }
            }
            foreach (var index in indices.Where(index => _blocks[index].Priority == Priority.Detail))
            {
                KeepBlock(index);
            }

            var body = new StringBuilder();
            foreach (var text in kept)
            {
                if (text is not null)
                {
                    body.AppendLine(text);
                }
            }
            if (omittedLines > 0)
            {
                body.AppendLine($"Output limit reached. {omittedLines} line(s) omitted.");
            }
            return body.ToString().TrimEnd();

            void KeepBlock(int index)
            {
                var (fullText, _, projectBlock, compactText) = _blocks[index];
                var text = compactText ?? fullText;
                var bytes = Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length;
                if (bytes <= remaining && (projectBlock is not { } anchor || kept[anchor] is not null))
                {
                    kept[index] = text;
                    remaining -= bytes;
                }
                else
                {
                    omittedLines += 1 + fullText.Count(character => character == '\n');
                }
            }
        }

        private readonly List<(string Text, Priority Priority, int? ProjectBlock, string? CompactText)> _blocks = [];
    }
}
