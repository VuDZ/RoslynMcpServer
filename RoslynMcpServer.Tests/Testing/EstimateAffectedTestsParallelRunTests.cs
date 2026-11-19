using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

[Trait("Category", TestCategories.TiaIntegration)]
[Collection("BaselineObservationGap")]
public sealed class EstimateAffectedTestsParallelRunTests
{
    [Fact]
    public async Task Parallel_run_finishes_builds_before_testing_and_keeps_report_order()
    {
        await using var fixture = await LoadAffectedAsync();
        var commands = new ConcurrentQueue<string>();
        var startedEarly = Gate();
        var bothTesting = Gate();
        var thirdTesting = Gate();
        var finishFirst = Gate();
        var finishSecond = Gate();
        var buildCount = 0;
        var activeTests = 0;

        async Task<DotNetCliRunner.RunResult> Runner(string command, string? directory, TimeSpan? timeout,
            int? previousExit, CancellationToken cancellationToken)
        {
            var project = Project(command);
            var build = IsBuild(command);
            commands.Enqueue(build ? $"build {project}" : $"test {project}");
            if (build)
            {
                buildCount++;
                return Success();
            }

            if (buildCount != 3)
            {
                startedEarly.TrySetResult();
            }
            if (Interlocked.Increment(ref activeTests) == 2)
            {
                bothTesting.TrySetResult();
            }
            try
            {
                switch (project)
                {
                    case "First": await finishFirst.Task.WaitAsync(cancellationToken); break;
                    case "Second": await finishSecond.Task.WaitAsync(cancellationToken); break;
                    case "Third": thirdTesting.TrySetResult(); break;
                }
                return Success();
            }
            finally
            {
                Interlocked.Decrement(ref activeTests);
            }
        }

        var operation = RunAsync(Tools(fixture, Runner), 2);
        try
        {
            var firstSignal = await Task.WhenAny(bothTesting.Task, startedEarly.Task).WaitAsync(Deadline);
            Assert.Same(bothTesting.Task, firstSignal);
            Assert.Equal(["build First", "build Second", "build Third", "test First", "test Second"], commands.ToArray());
            Assert.False(thirdTesting.Task.IsCompleted);
            finishSecond.SetResult();
            await thirdTesting.Task.WaitAsync(Deadline);
        }
        finally
        {
            finishFirst.TrySetResult();
            finishSecond.TrySetResult();
            await operation.WaitAsync(Deadline);
        }

        var response = await operation;
        Assert.Equal(["First", "Second", "Third"], ProjectLines(response).Select(Project));
        Assert.Equal(3, OutcomeCount(response, "passed"));
    }

    [Fact]
    public async Task Explicit_one_keeps_each_build_next_to_its_test()
    {
        await using var fixture = await LoadAffectedAsync();
        var commands = new List<string>();
        var tools = Tools(fixture, (command, _, _, _, _) =>
        {
            commands.Add((IsBuild(command) ? "build " : "test ") + Project(command));
            return Task.FromResult(Success());
        });

        var result = await RunAsync(tools, 1);

        Assert.Equal(["build First", "test First", "build Second", "test Second", "build Third", "test Third"], commands);
        Assert.Equal(3, OutcomeCount(result, "passed"));
    }

    [Fact]
    public async Task Failure_and_timeout_do_not_prevent_the_last_project_from_running()
    {
        await using var fixture = await LoadAffectedAsync();
        var tools = Tools(fixture, (command, _, timeout, _, _) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(300), timeout);
            return Task.FromResult(IsBuild(command) ? Success() : Project(command) switch
            {
                "First" => Success() with { ExitCode = 1, CombinedOutput = "Failed: 1, Passed: 0, Skipped: 0, Total: 1" },
                "Second" => Success() with { ExitCode = -1, TimedOut = true, ProcessKilled = true },
                _ => Success(),
            });
        });

        var result = await RunAsync(tools, 2);

        Assert.Equal(["failed", "timed out", "passed"], Outcomes(result));
    }

    [Fact]
    public async Task Cancellation_reaches_every_active_test_and_does_not_start_the_queued_test()
    {
        await using var fixture = await LoadAffectedAsync();
        using var cancellation = new CancellationTokenSource();
        var entered = Gate();
        var started = new ConcurrentQueue<string>();
        var cancelled = new ConcurrentQueue<string>();
        var directories = new ConcurrentQueue<string>();
        var tools = Tools(fixture, async (command, _, _, _, token) =>
        {
            if (IsBuild(command))
            {
                return Success();
            }

            var directory = ResultsDirectory(command);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "partial.trx"), "Partial test results");
            directories.Enqueue(directory);
            var project = Project(command);
            started.Enqueue(project);
            if (started.Count == 2)
            {
                entered.TrySetResult();
            }
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Success();
            }
            finally
            {
                cancelled.Enqueue(project);
            }
        });

        var operation = RunAsync(tools, 2, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Deadline);
        }
        finally
        {
            await cancellation.CancelAsync();
            await operation.WaitAsync(Deadline);
        }

        Assert.Equal(["First", "Second"], started.ToArray());
        Assert.Equal(["First", "Second"], cancelled.Order(StringComparer.Ordinal));
        Assert.Equal(["cancelled", "cancelled", "not run"], Outcomes(await operation));
        Assert.All(directories, directory => Assert.False(Directory.Exists(directory), directory));
    }

    [Fact]
    public async Task Session_history_starts_the_longest_test_first_on_the_next_run()
    {
        await using var fixture = await LoadAffectedAsync();
        var order = new ConcurrentQueue<string>();
        var tools = Tools(fixture, (command, _, _, _, _) =>
        {
            if (!IsBuild(command))
            {
                order.Enqueue(Project(command));
            }
            return Task.FromResult(Success() with
            {
                Elapsed = TimeSpan.FromSeconds(Project(command) switch { "Third" => 9, "Second" => 5, _ => 1 }),
            });
        });

        Assert.Equal(3, OutcomeCount(await RunAsync(tools, 2), "passed"));
        Assert.Equal(["First", "Second", "Third"], order.ToArray());
        order.Clear();

        Assert.Equal(3, OutcomeCount(await RunAsync(tools, 2), "passed"));
        Assert.Equal(["Third", "Second", "First"], order.ToArray());
    }

    [Fact]
    public async Task Real_parallel_projects_produce_independent_results_and_match_sequential_outcomes()
    {
        await using var fixture = new TestImpactRunFixture();
        await fixture.LoadAsync();
        await fixture.ChangeNonSourceInputAsync("First");
        await fixture.ChangeNonSourceInputAsync("Second");
        var runs = new ConcurrentDictionary<string, (string Directory, string Results)>();
        var tools = Tools(fixture, async (command, workDir, timeout, _, token) =>
        {
            var result = await DotNetCliRunner.RunWithMetadataAsync(command, workDir, token, timeout);
            if (!IsBuild(command))
            {
                var directory = ResultsDirectory(command);
                Assert.True(runs.TryAdd(Project(command), (directory, ReadTrx(directory))));
            }
            return result;
        });

        Assert.Equal(2, OutcomeCount(await RunAsync(tools, 1), "passed"));
        Assert.Equal(2, runs.Count);
        var sequential = runs.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value.Results).ToArray();
        var firstRunPaths = runs.Values.Select(value => value.Directory).ToArray();
        Assert.All(firstRunPaths, directory => Assert.False(Directory.Exists(directory), directory));
        runs.Clear();

        var response = await RunAsync(tools, 2);

        Assert.Equal(2, OutcomeCount(response, "passed"));
        Assert.Contains("Test execution: parallel (limit 2 projects).", response, StringComparison.Ordinal);
        Assert.Equal(2, runs.Count);
        var parallelPaths = runs.Values.Select(value => value.Directory).ToArray();
        Assert.All(parallelPaths, directory => Assert.False(Directory.Exists(directory), directory));
        Assert.Equal(4, firstRunPaths.Concat(parallelPaths).Distinct(InputPathCanon.Comparer).Count());
        Assert.Equal(sequential, runs.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value.Results));
    }

    private static async Task<TestImpactRunFixture> LoadAffectedAsync()
    {
        var fixture = new TestImpactRunFixture(includeThirdProject: true);
        try
        {
            await fixture.LoadAsync();
            foreach (var project in new[] { "First", "Second", "Third" })
            {
                await fixture.ChangeNonSourceInputAsync(project);
            }
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static Task<string> RunAsync(TestTools tools, int degree, CancellationToken token = default) =>
        tools.EstimateAffectedTests(run: true, maxParallelProjects: degree, cancellationToken: token);

    private static TestTools Tools(TestImpactRunFixture fixture, TestDllEnsure.Runner runner) =>
        new(fixture.Manager, NullLogger<TestTools>.Instance, runner);

    private static DotNetCliRunner.RunResult Success() =>
        new(0, "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1", "", 0, 0);

    private static bool IsBuild(string command) => command.StartsWith("build ", StringComparison.Ordinal);

    private static string ResultsDirectory(string command)
    {
        var match = Regex.Match(command, "--results-directory \"([^\"]+)\"");
        Assert.True(match.Success, "The test command did not isolate results.");
        return match.Groups[1].Value;
    }

    private static string Project(string text) => Regex.Match(text, @"\b(First|Second|Third)\.csproj").Groups[1].Value;

    private static string[] ProjectLines(string result) => result.Split(Environment.NewLine)
        .Where(line => line.StartsWith("Run: ", StringComparison.Ordinal)).ToArray();

    private static string[] Outcomes(string result) => result.Split(Environment.NewLine)
        .Where(line => line.StartsWith("Outcome: ", StringComparison.Ordinal)).Select(line => line[9..]).ToArray();

    private static int OutcomeCount(string result, string outcome) => Outcomes(result).Count(value => value == outcome);

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string ReadTrx(string directory)
    {
        var trx = XDocument.Load(Assert.Single(Directory.GetFiles(directory, "*.trx")));
        var results = trx.Descendants().Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => $"{element.Attribute("testName")?.Value}={element.Attribute("outcome")?.Value}")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, results.Length);
        return string.Join('\n', results);
    }

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
}
