using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestDllEnsureTests
{
    [Fact]
    public async Task First_dll_call_with_an_existing_dll_plans_a_solution_target_build()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(ExpectedBuild(sln), result.BuildArguments);
        Assert.DoesNotContain("AppTests.csproj", result.BuildArguments, StringComparison.Ordinal);
        Assert.True(result.TestsStarted);
        Assert.Equal(TestDllEnsure.ReasonUnknown, result.Reason);
        Assert.Equal("test-log", result.TestRun!.CombinedOutput);
        Assert.Equal("build-log", result.BuildRun!.CombinedOutput);
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Second_auto_call_still_builds_when_coverage_is_complete_and_hashes_are_absent()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true, coverage: InputCoverage.Complete);
        var first = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));
        var proof = held.State.GetProof(held.ProjectId, held.Context);
        Assert.NotNull(proof);
        Assert.Equal(InputCoverage.Complete, proof.Cursor.Coverage);
        Assert.Empty(proof.Cursor.InputContentHashes);
        var decision = held.State.EvaluateReuse(held.ProjectId, held.Context);
        Assert.False(decision.SkipAllowed);
        Assert.NotEqual(BuildFreshnessState.Current, decision.State);
        Assert.Contains(BuildReuseBlocker.InputContentHashAbsent, decision.Blockers);
        Assert.DoesNotContain(BuildReuseBlocker.CoverageUnknown, decision.Blockers);

        var second = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(ExpectedBuild(sln), first.BuildArguments);
        Assert.Equal(ExpectedBuild(sln), second.BuildArguments);
        Assert.True(second.TestsStarted);
        Assert.Equal(TestDllEnsure.ReasonUnknown, second.Reason);
        Assert.Equal(2, held.Runner.BuildCount);
    }

    [Fact]
    public async Task Second_auto_call_still_builds_when_coverage_stays_unknown()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true, coverage: InputCoverage.Unknown);
        await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));
        var proof = held.State.GetProof(held.ProjectId, held.Context);
        Assert.NotNull(proof);
        Assert.Equal(InputCoverage.Unknown, proof.Cursor.Coverage);
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));

        held.Runner.Clear();
        var second = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(ExpectedBuild(sln), second.BuildArguments);
        Assert.Equal(TestDllEnsure.ReasonCoverageUnknown, second.Reason);
        Assert.True(second.TestsStarted);
    }

    [Fact]
    public async Task Always_builds_and_never_does_not_start_tests_when_reuse_is_not_current()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        var never = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Never));
        Assert.False(never.TestsStarted);
        Assert.Null(never.TestArguments);
        Assert.Null(never.BuildArguments);
        Assert.Empty(held.Runner.Arguments);
        Assert.Contains("never", never.Error, StringComparison.Ordinal);
        Assert.Equal(TestDllEnsure.ReasonUnknown, never.Reason);

        var always = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Always));
        Assert.Equal(ExpectedBuild(sln), always.BuildArguments);
        Assert.True(always.TestsStarted);
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));

        held.Runner.Clear();
        var neverAfterBuild = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Never));
        Assert.False(neverAfterBuild.TestsStarted);
        Assert.Empty(held.Runner.Arguments);
        Assert.Contains("never", neverAfterBuild.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dll_test_uses_no_build_and_the_pre_build_is_the_solution_target()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true, noRestore: false, filter: "FullyQualifiedName~Sample");
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(ExpectedBuild(sln), result.BuildArguments);
        Assert.DoesNotContain("--no-restore", result.BuildArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("AppTests.csproj", result.BuildArguments, StringComparison.Ordinal);
        Assert.Equal(
            $"test \"{dll}\" --logger \"console;verbosity=normal\" --verbosity normal -c \"Sit-Debug\" -p:Platform=\"x64\" --no-build --no-restore --filter \"FullyQualifiedName~Sample\"",
            result.TestArguments);
        Assert.DoesNotContain("AppTests.csproj", result.TestArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-t:", result.TestArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("TreatWarningsAsErrors", result.TestArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetFramework", result.TestArguments, StringComparison.Ordinal);
        Assert.Equal(tree.Root, held.Runner.Directories[0]);
        Assert.Equal(Path.Combine(tree.Root, "tests"), held.Runner.Directories[1]);
    }

    [Fact]
    public async Task Caller_no_restore_is_on_the_solution_build_only()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true, noRestore: true);
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.EndsWith(" --no-restore", result.BuildArguments, StringComparison.Ordinal);
        Assert.Contains("--no-build --no-restore", result.TestArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore --no-restore", result.TestArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_solution_target_does_not_produce_a_test_command()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", AmbiguousSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true, needle: "App");
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.Empty(held.Runner.Arguments);
        Assert.Contains("matches 2 projects", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_solution_does_not_produce_a_test_command()
    {
        using var tree = new TempTree();
        var missing = Path.Combine(tree.Root, "Missing.sln");
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(missing, dll, exists: true);
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Always));

        Assert.False(result.TestsStarted);
        Assert.Empty(held.Runner.Arguments);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Project_file_is_not_the_dll_pre_build()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(csproj, dll, exists: true);
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Always));

        Assert.False(result.TestsStarted);
        Assert.Null(result.BuildArguments);
        Assert.Empty(held.Runner.Arguments);
        Assert.Contains(".sln", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Build_failure_does_not_produce_a_test_command()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        held.Runner.BuildExitCode = 1;
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.Null(result.TestRun);
        var command = Assert.Single(held.Runner.Arguments);
        Assert.Equal(ExpectedBuild(sln), command);
        Assert.Contains("not started", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Missing_output_does_not_produce_a_test_command()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: false);
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        var command = Assert.Single(held.Runner.Arguments);
        Assert.StartsWith("build ", command, StringComparison.Ordinal);
        Assert.Equal(TestDllEnsure.ReasonOutputMissing, result.Reason);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Command_name_is_not_a_proof()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        Assert.False(held.State.TryRecordInvocation("execute_dotnet_command"));
        Assert.False(held.State.TryRecordInvocation("dotnet build"));
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));

        held.Runner.BuildExitCode = 1;
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(ExpectedBuild(sln), result.BuildArguments);
        Assert.False(result.TestsStarted);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Changed_context_plans_a_build_and_reports_context_changed()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));
        var release = new BuildContext("Release", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);
        held.Runner.Clear();
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto, release));

        Assert.Equal(TestDllEnsure.ReasonContextChanged, result.Reason);
        Assert.Contains("-p:Configuration=\"Release\"", result.BuildArguments, StringComparison.Ordinal);
        Assert.Contains($"build \"{sln}\"", result.BuildArguments, StringComparison.Ordinal);
        Assert.True(result.TestsStarted);
        Assert.False(held.State.EvaluateReuse(held.ProjectId, release).SkipAllowed);
    }

    [Fact]
    public async Task Dirty_inputs_plan_a_build_and_report_dirty()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var generation = Guid.NewGuid();
        var projectId = ProjectId.CreateNewId();
        var baseline = Snapshot(generation, projectId, revision: 1, InputCoverage.Complete);
        var edited = Snapshot(generation, projectId, revision: 2, InputCoverage.Complete);
        var context = new BuildContext("Sit-Debug", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);
        var state = new SessionBuildState();
        state.Observe(baseline);
        var runner = new ScriptedRunner();
        var ensure = new TestDllEnsure(state, runner.Run);
        var result = await ensure.ExecuteAsync(new TestDllEnsureRequest
        {
            ProjectId = projectId,
            Context = context,
            Policy = TestBuildPolicy.Auto,
            PullSnapshot = () => edited,
            SolutionPath = sln,
            ProjectNeedle = "AppTests",
            AssemblyPath = dll,
            AssemblyExists = _ => true,
            BuildWorkingDirectory = tree.Root,
            TestWorkingDirectory = Path.Combine(tree.Root, "tests"),
            Timeout = TimeSpan.FromSeconds(12),
        });

        Assert.Equal(TestDllEnsure.ReasonDirty, result.Reason);
        Assert.Equal(ExpectedBuild(sln), result.BuildArguments);
        Assert.True(result.TestsStarted);
    }

    [Fact]
    public async Task Build_timeout_does_not_start_tests_or_refresh_the_budget()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        held.Runner.BuildTimedOut = true;
        var result = await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        var command = Assert.Single(held.Runner.Arguments);
        Assert.StartsWith("build ", command, StringComparison.Ordinal);
        var budget = Assert.Single(held.Runner.Timeouts);
        Assert.NotNull(budget);
        Assert.InRange(budget.Value, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(12));
        Assert.NotEqual(TimeSpan.FromSeconds(DotNetCliRunner.DefaultTimeoutSeconds), budget.Value);
        Assert.True(result.TimedOut);
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Successful_run_passes_the_remaining_budget_to_the_test_process()
    {
        using var tree = new TempTree();
        var sln = tree.Write("App.sln", SingleProjectSln);
        var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
        var held = Hold(sln, dll, exists: true);
        await held.Ensure.ExecuteAsync(held.Request(TestBuildPolicy.Auto));

        Assert.Equal(2, held.Runner.Timeouts.Count);
        Assert.All(held.Runner.Timeouts, timeout =>
        {
            Assert.NotNull(timeout);
            Assert.InRange(timeout.Value, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(12));
        });
        Assert.True(held.Runner.Timeouts[1] <= held.Runner.Timeouts[0]);
    }

    private const string SingleProjectSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTests", "AppTests.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    private const string AmbiguousSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "tests\App\App.csproj", "{44444444-4444-4444-4444-444444444444}"
        EndProject
        Global
        	GlobalSection(NestedProjects) = preSolution
        		{11111111-1111-1111-1111-111111111111} = {AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}
        		{44444444-4444-4444-4444-444444444444} = {CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}
        	EndGlobalSection
        EndGlobal
        """;

    private static string ExpectedBuild(string sln) =>
        $"build \"{sln}\" -t:\"AppTests\" -p:Configuration=\"Sit-Debug\" -p:Platform=\"x64\" -p:TargetFramework=\"net10.0\" -p:TreatWarningsAsErrors=false";

    private static Held Hold(
        string solutionPath,
        string dll,
        bool exists,
        InputCoverage coverage = InputCoverage.Complete,
        bool noRestore = false,
        string? filter = null,
        string needle = "AppTests")
    {
        var generation = Guid.NewGuid();
        var projectId = ProjectId.CreateNewId();
        var snapshot = Snapshot(generation, projectId, revision: 1, coverage);
        var context = new BuildContext("Sit-Debug", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);
        var state = new SessionBuildState();
        var runner = new ScriptedRunner();
        var root = Path.GetDirectoryName(solutionPath)!;
        var ensure = new TestDllEnsure(state, runner.Run);
        return new Held(state, runner, ensure, projectId, context, snapshot, solutionPath, dll, exists, noRestore, filter, needle, root);
    }

    private static WorkspaceInputSnapshot Snapshot(
        Guid generation,
        ProjectId projectId,
        int revision,
        InputCoverage coverage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", projectId.Id.ToString());
        var projectFile = Path.Combine(directory, "App.csproj");
        var instance = new LoadedProjectInstance(
            projectId,
            projectFile,
            "App",
            "App",
            Array.Empty<ProjectId>(),
            new[] { "net10.0" });
        var region = new MembershipRegion(directory, Recursive: true, projectId, "project-directory", CompletenessKnown: true);
        var input = new InputPathSnapshot(
            Path.Combine(directory, "Program.cs"),
            revision,
            outputRevision: 0,
            present: true,
            pending: false,
            new[] { projectId },
            new[] { InputRole.UserInput },
            Array.Empty<ProjectId>());
        var gaps = coverage == InputCoverage.Complete
            ? Array.Empty<InputCoverageGap>()
            : new[] { new InputCoverageGap(InputCoverageReason.LoadIncomplete, InputCoverageScope.WholeGraph) };
        return new WorkspaceInputSnapshot(
            generation,
            snapshotRevision: revision,
            membershipRevision: 1,
            new[] { projectFile },
            new[] { instance },
            new[] { region },
            new[] { input },
            coverage,
            gaps,
            evidenceSource: "loaded-graph",
            hasPendingContent: false);
    }

    private sealed class Held
    {
        public Held(
            SessionBuildState state,
            ScriptedRunner runner,
            TestDllEnsure ensure,
            ProjectId projectId,
            BuildContext context,
            WorkspaceInputSnapshot snapshot,
            string solutionPath,
            string dll,
            bool exists,
            bool noRestore,
            string? filter,
            string needle,
            string root)
        {
            State = state;
            Runner = runner;
            Ensure = ensure;
            ProjectId = projectId;
            Context = context;
            Snapshot = snapshot;
            SolutionPath = solutionPath;
            Dll = dll;
            Exists = exists;
            NoRestore = noRestore;
            Filter = filter;
            Needle = needle;
            Root = root;
        }

        public SessionBuildState State { get; }

        public ScriptedRunner Runner { get; }

        public TestDllEnsure Ensure { get; }

        public ProjectId ProjectId { get; }

        public BuildContext Context { get; }

        public WorkspaceInputSnapshot Snapshot { get; }

        public string SolutionPath { get; }

        public string Dll { get; }

        public bool Exists { get; }

        public bool NoRestore { get; }

        public string? Filter { get; }

        public string Needle { get; }

        public string Root { get; }

        public TestDllEnsureRequest Request(TestBuildPolicy policy, BuildContext? context = null)
        {
            return new TestDllEnsureRequest
            {
                ProjectId = ProjectId,
                Context = context ?? Context,
                Policy = policy,
                PullSnapshot = () => Snapshot,
                SolutionPath = SolutionPath,
                ProjectNeedle = Needle,
                AssemblyPath = Dll,
                AssemblyExists = _ => Exists,
                NoRestore = NoRestore,
                Filter = Filter,
                BuildWorkingDirectory = Root,
                TestWorkingDirectory = Path.Combine(Root, "tests"),
                Timeout = TimeSpan.FromSeconds(12),
            };
        }
    }

    private sealed class ScriptedRunner
    {
        public List<string> Arguments { get; } = new();

        public List<string?> Directories { get; } = new();

        public List<TimeSpan?> Timeouts { get; } = new();

        public int BuildExitCode { get; set; }

        public bool BuildTimedOut { get; set; }

        public int BuildCount => Arguments.Count(argument => argument.StartsWith("build ", StringComparison.Ordinal));

        public void Clear()
        {
            Arguments.Clear();
            Directories.Clear();
            Timeouts.Clear();
        }

        public Task<DotNetCliRunner.RunResult> Run(
            string arguments,
            string? workingDirectory,
            TimeSpan? timeout,
            int? previousExit,
            CancellationToken cancellationToken)
        {
            Arguments.Add(arguments);
            Directories.Add(workingDirectory);
            Timeouts.Add(timeout);
            if (arguments.StartsWith("build ", StringComparison.Ordinal))
            {
                return Task.FromResult(new DotNetCliRunner.RunResult(
                    BuildExitCode,
                    "build-log",
                    "build-meta",
                    0,
                    0,
                    BuildTimedOut));
            }

            return Task.FromResult(new DotNetCliRunner.RunResult(0, "test-log", "test-meta", 0, 0));
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
