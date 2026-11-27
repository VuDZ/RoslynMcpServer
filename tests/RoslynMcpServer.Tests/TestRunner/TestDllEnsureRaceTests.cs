using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestDllEnsureRaceTests
{
    [Fact]
    public async Task Edit_before_build_does_not_launch_the_dll_until_the_build_is_confirmed()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Revision = 1;
        held.State.Observe(held.Pull());
        held.Revision = 2;
        held.Runner.BuildExitCode = 1;

        var failed = await held.Ensure.ExecuteAsync(held.Request());

        Assert.False(failed.TestsStarted);
        Assert.Null(failed.TestArguments);
        var failedCommand = Assert.Single(held.Runner.Arguments);
        Assert.StartsWith("build ", failedCommand, StringComparison.Ordinal);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));

        held.Runner.BuildExitCode = 0;
        held.Runner.Arguments.Clear();
        var confirmed = await held.Ensure.ExecuteAsync(held.Request());

        Assert.Equal(TestDllEnsure.ReasonDirty, confirmed.Reason);
        Assert.True(confirmed.TestsStarted);
        Assert.Equal(2, held.Runner.Arguments.Count);
        Assert.StartsWith("build ", held.Runner.Arguments[0], StringComparison.Ordinal);
        Assert.Contains("--no-build", held.Runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains(held.Dll, held.Runner.Arguments[1], StringComparison.Ordinal);
        Assert.False(held.State.EvaluateReuse(held.ProjectId, held.Context).SkipAllowed);
    }

    [Fact]
    public async Task Edit_during_build_rebuilds_once_and_does_not_test_the_first_output()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Runner.OnBuild = (_, _) =>
        {
            if (held.Runner.BuildCount == 1)
            {
                held.Revision = 2;
            }

            return Task.CompletedTask;
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Equal(3, held.Runner.Arguments.Count);
        Assert.StartsWith("build ", held.Runner.Arguments[0], StringComparison.Ordinal);
        Assert.StartsWith("build ", held.Runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("--no-build", held.Runner.Arguments[2], StringComparison.Ordinal);
        Assert.Contains(held.Dll, held.Runner.Arguments[2], StringComparison.Ordinal);
        Assert.True(held.Runner.Timeouts[1] <= held.Runner.Timeouts[0]);
        Assert.All(held.Runner.Timeouts, timeout => Assert.InRange(timeout!.Value, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(12)));
        Assert.False(held.State.EvaluateReuse(held.ProjectId, held.Context).SkipAllowed);
    }

    [Fact]
    public async Task Edit_after_recorded_success_rebuilds_once_before_tests_start()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.OnPull = pull =>
        {
            // The fourth pull is the check after the build is recorded and before the test process.
            if (pull == 4)
            {
                held.Revision = 2;
            }
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Contains("--no-build", held.Runner.Arguments[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("test ", held.Runner.Arguments[0], StringComparison.Ordinal);
        Assert.False(held.State.EvaluateReuse(held.ProjectId, held.Context).SkipAllowed);
    }

    [Fact]
    public async Task Change_after_the_rebuild_does_not_start_tests()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Runner.OnBuild = (_, _) =>
        {
            held.Revision++;
            return Task.CompletedTask;
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Equal(TestDllEnsure.RaceExhausted, result.Error);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Output_replaced_before_test_start_rebuilds_once()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        Directory.CreateDirectory(Path.GetDirectoryName(held.Dll)!);
        File.WriteAllBytes(held.Dll, new byte[] { 1, 2, 3 });
        held.Exists = _ => File.Exists(held.Dll);
        held.OnPull = pull =>
        {
            if (pull == 4)
            {
                File.WriteAllBytes(held.Dll, new byte[] { 9 });
            }
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Contains("--no-build", result.TestArguments, StringComparison.Ordinal);
        Assert.Contains(held.Dll, result.TestArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_replaced_again_after_the_rebuild_does_not_start_tests()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        Directory.CreateDirectory(Path.GetDirectoryName(held.Dll)!);
        File.WriteAllBytes(held.Dll, new byte[] { 1, 2, 3 });
        held.Exists = _ => File.Exists(held.Dll);
        held.OnPull = pull =>
        {
            if (pull >= 4)
            {
                File.WriteAllBytes(held.Dll, new byte[pull]);
            }
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Equal(TestDllEnsure.RaceExhausted, result.Error);
    }

    [Fact]
    public async Task Rebuild_is_refused_when_the_shared_budget_is_already_spent()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Timeout = TimeSpan.FromMilliseconds(200);
        held.Runner.BuildDelay = TimeSpan.FromMilliseconds(400);
        held.Runner.OnBuild = (_, _) =>
        {
            held.Revision = 2;
            return Task.CompletedTask;
        };

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.Equal(1, held.Runner.BuildCount);
        Assert.True(result.TimedOut);
        Assert.Equal(TestDllEnsure.RebuildBudgetExhausted, result.Error);
        var budget = Assert.Single(held.Runner.Timeouts);
        Assert.NotNull(budget);
        Assert.InRange(budget.Value, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(200));
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact(Timeout = 15000)]
    public async Task Same_context_does_not_overlap_and_a_success_does_not_let_the_waiter_skip()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        held.Runner.OnBuild = async (index, _) =>
        {
            if (index == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
        };

        var first = held.Ensure.ExecuteAsync(held.Request());
        await entered.Task;
        var second = held.Ensure.ExecuteAsync(held.Request());
        release.TrySetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.True(firstResult.TestsStarted);
        Assert.True(secondResult.TestsStarted);
        Assert.Equal(1, held.Runner.MaxInside);
        Assert.Equal(2, held.Runner.BuildCount);
        Assert.Equal(new[] { "build", "test", "build", "test" }, held.Runner.Kinds.ToArray());
        Assert.NotEqual(TestDllEnsure.ReasonConfirmedReuse, secondResult.Reason);
        Assert.False(held.State.EvaluateReuse(held.ProjectId, held.Context).SkipAllowed);
    }

    [Fact(Timeout = 15000)]
    public async Task Failed_in_flight_operation_is_not_the_waiters_proof()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Runner.BuildExitCode = 1;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        held.Runner.OnBuild = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var first = held.Ensure.ExecuteAsync(held.Request());
        await entered.Task;
        var second = held.Ensure.ExecuteAsync(held.Request());
        release.TrySetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.False(firstResult.TestsStarted);
        Assert.False(secondResult.TestsStarted);
        Assert.Null(secondResult.TestArguments);
        Assert.Equal(TestDllEnsure.WaiterRejected, secondResult.Error);
        Assert.Equal(1, held.Runner.BuildCount);
        Assert.Equal(1, held.Runner.MaxInside);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
        Assert.DoesNotContain(held.Runner.Arguments, argument => argument.StartsWith("test ", StringComparison.Ordinal));
    }

    [Fact(Timeout = 15000)]
    public async Task Waiter_timeout_does_not_cancel_or_kill_the_running_operation()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        held.Timeout = TimeSpan.FromSeconds(30);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        held.Runner.OnBuild = async (_, token) =>
        {
            observed = token;
            entered.TrySetResult();
            await release.Task;
        };

        var first = held.Ensure.ExecuteAsync(held.Request());
        await entered.Task;
        var waiter = Held.CreateRequest(held, TimeSpan.FromMilliseconds(200));
        var started = Stopwatch.StartNew();
        var second = await held.Ensure.ExecuteAsync(waiter);
        started.Stop();

        Assert.False(second.TestsStarted);
        Assert.Null(second.TestArguments);
        Assert.True(second.TimedOut);
        Assert.Equal(TestDllEnsure.ContextBusy, second.Error);
        Assert.InRange(started.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
        Assert.False(observed.IsCancellationRequested);
        Assert.False(release.Task.IsCompleted);
        release.TrySetResult();
        var firstResult = await first;
        Assert.True(firstResult.TestsStarted);
        Assert.False(held.State.TryRecordInvocation("execute_dotnet_command"));
        Assert.False(held.State.TryRecordInvocation("dotnet build"));
    }

    [Fact(Timeout = 15000)]
    public async Task Generation_change_is_not_the_waiters_proof_and_does_not_confirm_the_new_session()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        var replacement = held.SnapshotWith(Guid.NewGuid(), revision: 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        held.Runner.OnBuild = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var first = held.Ensure.ExecuteAsync(held.Request());
        await entered.Task;
        held.SnapshotOverride = replacement;
        var second = held.Ensure.ExecuteAsync(held.Request());
        release.TrySetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.False(firstResult.TestsStarted);
        Assert.Equal(TestDllEnsure.GenerationRejected, firstResult.Error);
        Assert.False(secondResult.TestsStarted);
        Assert.Equal(TestDllEnsure.WaiterRejected, secondResult.Error);
        Assert.Equal(1, held.Runner.BuildCount);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));
        Assert.False(held.State.TryRecordBuildResult(
            held.ProjectId,
            held.Context,
            held.SnapshotWith(Guid.NewGuid(), revision: 1),
            succeeded: true,
            cancelled: false,
            held.Dll));
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact(Timeout = 15000)]
    public async Task Different_build_contexts_are_not_serialized_on_each_other()
    {
        using var tree = new TempTree();
        var held = Held.Create(tree);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        held.Runner.OnBuild = async (_, _) =>
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                both.TrySetResult();
            }

            await release.Task;
        };

        var other = new BuildContext("Release", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", held.Dll);
        var first = held.Ensure.ExecuteAsync(held.Request());
        var second = held.Ensure.ExecuteAsync(held.Request(other));
        var entered = await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Same(both.Task, entered);
        Assert.Equal(2, held.Runner.MaxInside);
    }

    [Fact(Timeout = 5000)]
    public async Task Generation_check_during_ensure_does_not_take_the_workspace_semaphore()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var manager = SolutionManagerTestFactory.Create();
        var projectFile = Path.Combine(tree.Root, "AppTests.csproj");
        manager.StartDiskWatcherSessionForTests(new[] { projectFile });
        var semaphore = (SemaphoreSlim)typeof(SolutionManager)
            .GetField("_workspaceLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(manager)!;
        var runner = new ScriptedRunner();
        var ensure = new TestDllEnsure(manager.SessionBuildState, runner.Run);
        await semaphore.WaitAsync();
        try
        {
            var result = await ensure.ExecuteAsync(new TestDllEnsureRequest
            {
                ProjectId = ProjectId.CreateNewId(),
                Context = new BuildContext("Debug", null, "net10.0", null, dll),
                Policy = TestBuildPolicy.Auto,
                PullSnapshot = manager.PullInputSnapshot,
                SolutionPath = sln,
                ProjectNeedle = "AppTests",
                AssemblyPath = dll,
                AssemblyExists = _ => true,
                BuildWorkingDirectory = tree.Root,
                TestWorkingDirectory = tree.Root,
                Timeout = TimeSpan.FromSeconds(12),
            });

            Assert.True(result.TestsStarted);
            Assert.Contains("--no-build", result.TestArguments, StringComparison.Ordinal);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private const string SingleProjectSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTests", "AppTests.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    private sealed class Held
    {
        private int _pulls;

        private Held(
            SessionBuildState state,
            ScriptedRunner runner,
            TestDllEnsure ensure,
            ProjectId projectId,
            BuildContext context,
            string solutionPath,
            string dll,
            string root)
        {
            State = state;
            Runner = runner;
            Ensure = ensure;
            ProjectId = projectId;
            Context = context;
            SolutionPath = solutionPath;
            Dll = dll;
            Root = root;
            Generation = Guid.NewGuid();
            Revision = 1;
            Exists = _ => true;
            Timeout = TimeSpan.FromSeconds(12);
        }

        public SessionBuildState State { get; }

        public ScriptedRunner Runner { get; }

        public TestDllEnsure Ensure { get; }

        public ProjectId ProjectId { get; }

        public BuildContext Context { get; }

        public string SolutionPath { get; }

        public string Dll { get; }

        public string Root { get; }

        public Guid Generation { get; }

        public int Revision { get; set; }

        public Func<string, bool> Exists { get; set; }

        public TimeSpan Timeout { get; set; }

        public WorkspaceInputSnapshot? SnapshotOverride { get; set; }

        public Action<int>? OnPull { get; set; }

        public static Held Create(TempTree tree)
        {
            var sln = tree.Write("App.sln", SingleProjectSln);
            var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
            var projectId = ProjectId.CreateNewId();
            var context = new BuildContext("Sit-Debug", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);
            var state = new SessionBuildState();
            var runner = new ScriptedRunner();
            var ensure = new TestDllEnsure(state, runner.Run);
            return new Held(state, runner, ensure, projectId, context, sln, dll, tree.Root);
        }

        public WorkspaceInputSnapshot Pull()
        {
            _pulls++;
            OnPull?.Invoke(_pulls);
            return SnapshotOverride ?? SnapshotWith(Generation, Revision);
        }

        public WorkspaceInputSnapshot SnapshotWith(Guid generation, int revision)
        {
            var directory = Path.Combine(Root, "src");
            var projectFile = Path.Combine(directory, "App.csproj");
            var instance = new LoadedProjectInstance(
                ProjectId,
                projectFile,
                "AppTests",
                "AppTests",
                Array.Empty<ProjectId>(),
                new[] { "net10.0" });
            var region = new MembershipRegion(directory, Recursive: true, ProjectId, "project-directory", CompletenessKnown: true);
            var input = new InputPathSnapshot(
                Path.Combine(directory, "Program.cs"),
                revision,
                outputRevision: 0,
                present: true,
                pending: false,
                new[] { ProjectId },
                new[] { InputRole.UserInput },
                Array.Empty<ProjectId>());
            return new WorkspaceInputSnapshot(
                generation,
                snapshotRevision: revision,
                membershipRevision: 1,
                new[] { projectFile },
                new[] { instance },
                new[] { region },
                new[] { input },
                InputCoverage.Complete,
                Array.Empty<InputCoverageGap>(),
                evidenceSource: "loaded-graph",
                hasPendingContent: false);
        }

        public TestDllEnsureRequest Request(BuildContext? context = null)
        {
            return CreateRequest(this, Timeout, context);
        }

        public static TestDllEnsureRequest CreateRequest(Held held, TimeSpan timeout, BuildContext? context = null)
        {
            return new TestDllEnsureRequest
            {
                ProjectId = held.ProjectId,
                Context = context ?? held.Context,
                Policy = TestBuildPolicy.Auto,
                PullSnapshot = held.Pull,
                SolutionPath = held.SolutionPath,
                ProjectNeedle = "AppTests",
                AssemblyPath = held.Dll,
                AssemblyExists = held.Exists,
                BuildWorkingDirectory = held.Root,
                TestWorkingDirectory = Path.Combine(held.Root, "tests"),
                Timeout = timeout,
            };
        }
    }

    private sealed class ScriptedRunner
    {
        private int _inside;
        private readonly object _overlap = new();

        public List<string> Arguments { get; } = new();

        public List<string> Kinds { get; } = new();

        public List<TimeSpan?> Timeouts { get; } = new();

        public int BuildExitCode { get; set; }

        public TimeSpan BuildDelay { get; set; }

        public int BuildCount { get; private set; }

        public int MaxInside { get; private set; }

        public Func<int, CancellationToken, Task>? OnBuild { get; set; }

        public async Task<DotNetCliRunner.RunResult> Run(
            string arguments,
            string? workingDirectory,
            TimeSpan? timeout,
            int? previousExit,
            CancellationToken cancellationToken)
        {
            Arguments.Add(arguments);
            Timeouts.Add(timeout);
            if (!arguments.StartsWith("build ", StringComparison.Ordinal))
            {
                Kinds.Add("test");
                return new DotNetCliRunner.RunResult(0, "test-log", "test-meta", 0, 0);
            }

            Kinds.Add("build");
            int index;
            lock (_overlap)
            {
                _inside++;
                if (_inside > MaxInside)
                {
                    MaxInside = _inside;
                }

                BuildCount++;
                index = BuildCount;
            }

            try
            {
                if (BuildDelay > TimeSpan.Zero)
                {
                    await Task.Delay(BuildDelay).ConfigureAwait(false);
                }

                if (OnBuild is not null)
                {
                    await OnBuild(index, cancellationToken).ConfigureAwait(false);
                }

                return new DotNetCliRunner.RunResult(BuildExitCode, "build-log", "build-meta", 0, 0);
            }
            finally
            {
                lock (_overlap)
                {
                    _inside--;
                }
            }
        }
    }

    private sealed class TempTree : IDisposable
    {
        public TempTree()
        {
            Root = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string name, string text)
        {
            var path = Path.Combine(Root, name);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
