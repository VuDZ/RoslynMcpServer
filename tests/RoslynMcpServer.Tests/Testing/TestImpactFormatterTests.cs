using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Tests.TestRunner;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class TestImpactFormatterTests
{
    [Fact]
    public void Long_filters_keep_each_projects_outcome_and_reasons_under_its_own_heading()
    {
        using var workspace = new AdhocWorkspace();
        var first = workspace.AddProject("First", LanguageNames.CSharp);
        var second = workspace.AddProject("Second", LanguageNames.CSharp);
        var third = workspace.AddProject("Third", LanguageNames.CSharp);
        var longName = new string('路', 25);
        var plans = new[] { first, second, third }.Select(project => new VstestProjectRunPlan(
            project.Id,
            string.Join("|", Enumerable.Range(0, 125)
                .Select(index => $"FullyQualifiedName~{project.Name}.{longName}.Check{index}")),
            [project.Name + " test selection."]))
            .ToArray();
        Assert.All(plans, plan => Assert.InRange(plan.Filter!.Length, 1, VstestRunPlanner.MaxFilterLength));
        Assert.True(plans.Sum(plan => Encoding.UTF8.GetByteCount(plan.Filter!)) > 32 * 1024);
        var impact = new TestImpactResult([], new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>>(), [],
            RequiresReload: false, ProjectGraphStale: false);
        var estimate = new TestImpactEstimate([], [], impact, plans, [], ["Project graph warning."], [])
        {
            UnknownBaselinePaths = ["Unknown.cs"],
        };
        const string Failure = "Third project assertion failed.";
        var runs = new Dictionary<ProjectId, TestImpactProjectRun>
        {
            [first.Id] = new(new DotNetTestResult(TestProcessOutcome.Passed, Trx: Trx(TestProcessOutcome.Passed, new(3, 3, 0, 0)))),
            [second.Id] = new(new DotNetTestResult(TestProcessOutcome.TimedOut)),
            [third.Id] = new(new DotNetTestResult(
                TestProcessOutcome.Failed,
                TestRun: new(1, "Failed: 1, Passed: 10, Skipped: 0, Total: 11", "", 0, 0) { OutputTruncated = true },
                Trx: Trx(TestProcessOutcome.Failed, new(11, 10, 1, 0), new VstestOutputParser.FailedTestDetail("Third.Test", Failure, "")))),
        };

        var response = TestImpactFormatter.Format(third.Solution, estimate, TestNarrowingResult.Empty,
            TestClassStageLevel.Changed, includeDetails: true, runs, "Test execution: sequential.");

        Assert.InRange(Encoding.UTF8.GetByteCount(response), 1, 32 * 1024);
        var sections = response.Split(Environment.NewLine + "Run: ", StringSplitOptions.None);
        Assert.Equal(4, sections.Length);
        Assert.StartsWith("`First` with filter", sections[1], StringComparison.Ordinal);
        Assert.Contains("Outcome: passed", sections[1], StringComparison.Ordinal);
        Assert.Contains("Total: 3; Passed: 3", sections[1], StringComparison.Ordinal);
        Assert.Contains("Reasons: First test selection.", sections[1], StringComparison.Ordinal);
        Assert.StartsWith("`Second` with filter", sections[2], StringComparison.Ordinal);
        Assert.Contains("Outcome: timed out", sections[2], StringComparison.Ordinal);
        Assert.Contains("Reasons: Second test selection.", sections[2], StringComparison.Ordinal);
        Assert.Contains("MCP_DOTNET_TIMEOUT", sections[2], StringComparison.Ordinal);
        Assert.StartsWith("`Third` with a filter omitted from this response.", sections[3], StringComparison.Ordinal);
        Assert.Contains("Outcome: failed", sections[3], StringComparison.Ordinal);
        Assert.Contains("Total: 11; Passed: 10; Failed: 1", sections[3], StringComparison.Ordinal);
        Assert.Contains("Reasons: Third test selection.", sections[3], StringComparison.Ordinal);
        Assert.Contains(Failure, sections[3], StringComparison.Ordinal);
        Assert.Contains(TruncatedProcessLog.CaptureMarker, sections[3], StringComparison.Ordinal);
        Assert.DoesNotContain("Showing first 5 failures only", sections[3], StringComparison.Ordinal);
        Assert.Contains("Baseline unknown in 1 file(s)", sections[3], StringComparison.Ordinal);
        Assert.Contains("Project graph warning.", sections[3], StringComparison.Ordinal);
        Assert.DoesNotContain("Outcome: failed", sections[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0, null, "passed", "Total: 1; Passed: 1; Failed: 0")]
    [InlineData(0, 1, null, "failed", "Total: 1; Passed: 0; Failed: 1")]
    [InlineData(0, 0, "FullyQualifiedName~Missing", "failed", "no matching tests")]
    public async Task Rapid_TIA_reads_the_outcome_from_TRX_when_console_output_is_clipped(
        int passed,
        int failed,
        string? filter,
        string expected,
        string reported)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Tests", LanguageNames.CSharp);
        var plans = new[] { new VstestProjectRunPlan(project.Id, Filter: filter, []) };
        var impact = new TestImpactResult([], new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>>(), [],
            RequiresReload: false, ProjectGraphStale: false);
        var estimate = new TestImpactEstimate([], [], impact, plans, [], [], []);
        var projectPath = Path.Combine(Path.GetTempPath(), "RoslynMcpTiaTrx", "Tests.csproj");
        var source = Path.ChangeExtension(projectPath, ".dll");
        var runner = new DotNetTestExecutor((command, _, _, _, _) =>
        {
            var directory = VstestTrxTestFiles.ResultsDirectory(command);
            VstestTrxTestFiles.WriteInventory(directory, [source], filter);
            VstestTrxTestFiles.WriteReport(directory, source, passed, failed, noMatchFilter: filter);
            // Clipped console text no longer decides the outcome.
            return Task.FromResult(new DotNetCliRunner.RunResult(failed > 0 ? 1 : 0, "clipped", "", 900_000, 0)
            {
                OutputTruncated = true,
            });
        });

        var result = await runner.TestWithTrxAsync(
            $"test \"{projectPath}\" --no-build" + (filter is null ? string.Empty : $" --filter \"{filter}\""),
            ".", null, null, assemblyPath: null, filter, CancellationToken.None);
        var runs = new Dictionary<ProjectId, TestImpactProjectRun> { [project.Id] = new(result) };
        var response = TestImpactFormatter.Format(project.Solution, estimate, TestNarrowingResult.Empty,
            TestClassStageLevel.Changed, includeDetails: false, runs, "Test execution: sequential.");

        Assert.Equal(expected, result.OutcomeText);
        Assert.Contains($"Outcome: {expected}", response, StringComparison.Ordinal);
        Assert.Contains(reported, response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Large_report_groups_reasons_and_caps_utf8_without_reducing_the_plan(bool includeDetails)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Tests", LanguageNames.CSharp);
        var longDirectory = new string('路', 80);
        var paths = Enumerable.Range(0, 500).Select(index => $"{longDirectory}/Test{index}.cs").ToArray();
        var expansion = paths.Select(path => new ProjectExpansionReason(ProjectExpansionKind.CSharpSource, project.Id, path))
            .ToArray();
        var impact = new TestImpactResult([], new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>>
        {
            [project.Id] = expansion,
        }, [], RequiresReload: false, ProjectGraphStale: false);
        var plans = VstestRunPlanner.Plan(impact, [], CancellationToken.None);
        var method = new BaselineMemberKey(SyntaxKind.MethodDeclaration, "Tests", "Method", 0, "", "");
        var change = new BaselineDocumentChange(BaselineChangeKind.MemberModified, BaselineChangeReason.MemberSyntax, method);
        const string UnknownPath = "Unknown.cs";
        var documents = paths.Select(path => new DocumentImpact(project.Id, path, null, [change]))
            .Append(new DocumentImpact(project.Id, UnknownPath, null, null)).ToArray();
        string[] errors = includeDetails ? ["CS0103: compiler error."] : paths.Select(path => $"CS0103: {path}").ToArray();
        var estimate = new TestImpactEstimate(documents, [.. paths, UnknownPath], impact, plans, [],
            ["Project graph warning."], errors)
        {
            UnknownBaselinePaths = [UnknownPath],
        };
        var runs = new Dictionary<ProjectId, TestImpactProjectRun>
        {
            [project.Id] = new(new DotNetTestResult(TestProcessOutcome.Passed)),
        };

        var response = TestImpactFormatter.Format(project.Solution, estimate, TestNarrowingResult.Empty,
            TestClassStageLevel.Changed, includeDetails, runs, "Test execution: sequential.");

        Assert.StartsWith(TestImpactFormatter.Warning, response, StringComparison.Ordinal);
        Assert.Contains("Changed since load: 500 members in 500 files", response, StringComparison.Ordinal);
        Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
        Assert.Contains("Test execution: sequential.", response, StringComparison.Ordinal);
        Assert.Contains("- Baseline unknown in 1 file(s): Unknown.cs", response, StringComparison.Ordinal);
        Assert.Contains("Project graph warning.", response, StringComparison.Ordinal);
        Assert.Contains("Project expansion without a call graph: 500 entries (examples:", response, StringComparison.Ordinal);
        Assert.Contains("CS0103", response, StringComparison.Ordinal);
        Assert.Contains("line(s) omitted.", response, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(response), 1, 32 * 1024);
        Assert.Equal(501, Assert.Single(plans).Reasons.Count);
        Assert.Null(Assert.Single(plans).Filter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_wider_stage_blocker_keeps_counts_visible_and_only_affects_its_stage_and_wider(bool wide)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Tests", LanguageNames.CSharp);
        const string Filter = "FullyQualifiedName~Cases.Direct.";
        var direct = new VstestTestClass(project.Id, [], "Cases.Direct", true, null, false);
        var fixture = new VstestTestClass(project.Id, [], "Cases.Fixture", true, null, true);
        TestClassSelection[] directClasses = [new(direct, TestClassSelectionReasons.ReachesChangedType)];
        TestClassSelection[] discoveryClasses = [.. directClasses, new(fixture, TestClassSelectionReasons.ReachesChangedType)];
        var whole = new TestNarrowingWholeReason(TestNarrowingWholeKind.FixtureArguments, "Cases.Fixture",
            "Cases.Fixture has fixture arguments.");
        TestClassStage[] stages =
        [
            new(TestClassStageLevel.Changed, [], null, null),
            new(TestClassStageLevel.References, directClasses, Filter, null),
            new(TestClassStageLevel.Discovery, discoveryClasses, null, whole),
            new(TestClassStageLevel.DiscoveryWithLoads, discoveryClasses, null, whole),
        ];
        var narrowing = new TestNarrowingResult([new TestProjectNarrowing(project.Id, TestNarrowingOutcome.Narrowed,
            null, stages, [new("Lib", "Lib.A")], [])]);
        var impact = new TestImpactResult([], new Dictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>>(), [],
            RequiresReload: false, ProjectGraphStale: false);
        var estimate = new TestImpactEstimate([], [], impact, [new(project.Id, null, ["Project selected."])],
            [direct, fixture], [], []);
        var depth = wide ? TestClassStageLevel.Discovery : TestClassStageLevel.References;

        var response = TestImpactFormatter.Format(project.Solution, estimate, narrowing, depth,
            includeDetails: true, runs: null, state: TestImpactDepth.State(depth));

        Assert.Contains("Stages (classes): changed=0, direct=1, di=2 (whole project), all=2 (whole project).",
            response, StringComparison.Ordinal);
        Assert.Contains("di (2, whole project): Cases.Direct, Cases.Fixture", response, StringComparison.Ordinal);
        if (wide)
        {
            Assert.Contains("whole project", response, StringComparison.Ordinal);
            Assert.Contains(whole.Message, response, StringComparison.Ordinal);
            Assert.DoesNotContain(Filter, response, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(Filter, response, StringComparison.Ordinal);
            Assert.DoesNotContain(whole.Message, response, StringComparison.Ordinal);
        }
    }

    private static VstestTrxResult Trx(
        TestProcessOutcome outcome,
        VstestOutputParser.TestSummary summary,
        params VstestOutputParser.FailedTestDetail[] failures) =>
        new(outcome, summary, failures, [], false, false, null, null);
}
