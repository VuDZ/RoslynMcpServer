using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class EstimateAffectedTestsRunTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Early_run_responses_keep_the_warning_state_and_final_limitations(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            cancellation.Cancel();
        }

        var response = await EstimateAffectedTestsTests.Tools(SolutionManagerTestFactory.Create())
            .EstimateAffectedTests(run: true, cancellationToken: cancellation.Token);

        AssertRun(response, cancelled ? "cancelled" : "No workspace loaded");
        Assert.EndsWith("Not covered: no execution report is available for this request.", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", "GC.KeepAlive(value); Assert.True(true);");
        var estimate = await fixture.Tools.EstimateAffectedTests();
        var line = Assert.Single(estimate.Split(Environment.NewLine), line => line.StartsWith("Run: ", StringComparison.Ordinal));
        var filter = line.Split(" with filter `", StringSplitOptions.None)[1].TrimEnd('`');
        Assert.Contains("FullyQualifiedName~Ns.Cases.First", filter, StringComparison.Ordinal);
        var manual = await fixture.Tools.RunTestByFilter(fixture.ProjectPath("First"), filter, noBuild: true);
        Assert.Contains("3", manual, StringComparison.Ordinal);
        var expected = new[] { "First1", "First2", "FirstExtra" };
        Assert.Equal(expected, File.ReadAllLines(fixture.MarkerPath("First")).Order(StringComparer.Ordinal));
        File.Delete(fixture.MarkerPath("First"));

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        output.WriteLine(response);
        AssertRun(response, "passed");
        Assert.Contains("Total: 3", response, StringComparison.Ordinal);
        Assert.Equal(expected, File.ReadAllLines(fixture.MarkerPath("First")).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(fixture.MarkerPath("Second")));
    }

    [Fact]
    public async Task Estimate_after_run_keeps_the_filter_and_a_restored_edit_leaves_an_empty_plan()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", "GC.KeepAlive(value); Assert.True(true);");
        var before = await fixture.Tools.EstimateAffectedTests();
        AssertRun(await fixture.Tools.EstimateAffectedTests(run: true), "passed");

        var after = await fixture.Tools.EstimateAffectedTests();
        await fixture.RestoreAsync("First");
        var restored = await fixture.Tools.EstimateAffectedTests();

        output.WriteLine(after);
        output.WriteLine(restored);
        Assert.Equal(RunLines(before), RunLines(after));
        Assert.Contains("Changed since load: 1 members in 1 files", after, StringComparison.Ordinal);
        Assert.DoesNotContain("whole project", after, StringComparison.Ordinal);
        Assert.DoesNotContain("First.dll", after, StringComparison.Ordinal);
        Assert.Contains("Changed since load: 0 members in 0 files", restored, StringComparison.Ordinal);
        Assert.Empty(RunLines(restored));
    }

    [Fact]
    public async Task Run_builds_changed_sources_instead_of_passing_on_stale_binaries_and_continues_after_test_failure()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", "Seen(\"NewSource\"); Assert.Fail(\"New source was executed\");");
        await fixture.ChangeAsync("Second", "GC.KeepAlive(value); Assert.True(true);");
        var stale = await fixture.Tools.RunTestByFilter(fixture.ProjectPath("First"), "FullyQualifiedName~Ns.Cases.First");
        Assert.Contains("## Filtered tests passed", stale, StringComparison.Ordinal);
        Assert.DoesNotContain("New source was executed", stale, StringComparison.Ordinal);
        Assert.Equal(new[] { "First1", "First2", "FirstExtra" },
            File.ReadAllLines(fixture.MarkerPath("First")).Order(StringComparer.Ordinal));
        File.Delete(fixture.MarkerPath("First"));

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        output.WriteLine(response);
        AssertRun(response, "failed");
        Assert.Contains("Failed: 2", response, StringComparison.Ordinal);
        Assert.Equal(2, File.ReadAllLines(fixture.MarkerPath("First")).Count(line => line == "NewSource"));
        Assert.Contains("Second.csproj", response, StringComparison.Ordinal);
        Assert.Contains("passed", response, StringComparison.Ordinal);
        Assert.Equal(3, File.ReadAllLines(fixture.MarkerPath("Second")).Length);
    }

    [Fact]
    public async Task Build_failure_does_not_start_old_tests_and_the_next_project_runs()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", "MissingType.Run();");
        await fixture.ChangeAsync("Second", "GC.KeepAlive(value); Assert.True(true);");

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        AssertRun(response, "build failed");
        Assert.Contains("CS0103", response, StringComparison.Ordinal);
        Assert.DoesNotContain("reportCursor", response, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.MarkerPath("First")));
        Assert.Equal(3, File.ReadAllLines(fixture.MarkerPath("Second")).Length);
        Assert.Contains("passed", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First",
            "_output.WriteLine(\"BEGIN-OUTPUT\" + new string('x', 6000) + \"END-OUTPUT\"); Assert.Fail(\"Output failure\");");

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        AssertRun(response, "failed");
        Assert.Contains("truncated to 2500 chars", response, StringComparison.Ordinal);
        Assert.Contains("BEGIN-OUTPUT", response, StringComparison.Ordinal);
        Assert.Contains("END-OUTPUT", response, StringComparison.Ordinal);
        Assert.DoesNotContain("includeFullOutput", response, StringComparison.Ordinal);
        Assert.DoesNotContain("maxOutputChars", response, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 3000), response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whole_project_plan_runs_without_a_filter()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ExpandProjectAsync("First");

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        AssertRun(response, "passed");
        Assert.Contains("whole project", response, StringComparison.Ordinal);
        Assert.Contains("Total: 4", response, StringComparison.Ordinal);
        Assert.Equal(4, File.ReadAllLines(fixture.MarkerPath("First")).Length);
        Assert.False(File.Exists(fixture.MarkerPath("Second")));
    }

    [Fact]
    public async Task Timed_out_project_is_not_a_failed_test_and_the_next_project_runs()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", fixture.HangingBody());
        await fixture.ChangeAsync("Second", "GC.KeepAlive(value); Assert.True(true);");
        int[] ids = [];
        try
        {
            var operation = fixture.Tools.EstimateAffectedTests(run: true, timeoutSeconds: 20);
            ids = await fixture.WaitForProcessesAsync(operation);
            var response = await operation;

            output.WriteLine(response);
            AssertRun(response, "timed out");
            Assert.DoesNotContain("Outcome: failed", response, StringComparison.Ordinal);
            Assert.Equal(3, File.ReadAllLines(fixture.MarkerPath("Second")).Length);
            await TestImpactRunFixture.AssertStoppedAsync(ids);
        }
        finally
        {
            TestImpactRunFixture.Kill(ids);
        }
    }

    [Fact]
    public async Task Empty_run_has_no_process_reports_and_keeps_the_warning_and_limitations()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        AssertRun(response, "No test processes selected");
        Assert.False(File.Exists(fixture.MarkerPath("First")));
        Assert.False(File.Exists(fixture.MarkerPath("Second")));
    }

    internal static void AssertRun(string response, string outcome)
    {
        Assert.StartsWith(EstimateAffectedTestsTests.Warning + Environment.NewLine, response, StringComparison.Ordinal);
        Assert.Contains(outcome, response, StringComparison.Ordinal);
        Assert.Contains("project-level result only", response, StringComparison.Ordinal);
        Assert.True(response.LastIndexOf("Not covered", StringComparison.Ordinal) > response.LastIndexOf(outcome, StringComparison.Ordinal),
            "Run outcomes must precede Not covered: " + response);
    }

    private static string[] RunLines(string response) =>
        response.Split(Environment.NewLine).Where(line => line.StartsWith("Run: ", StringComparison.Ordinal)).ToArray();
}
