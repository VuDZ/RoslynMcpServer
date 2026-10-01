using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestDllEnsureAcceptanceTests
{
    [Fact]
    public async Task Timed_out_build_leaves_an_existing_dll_unknown()
    {
        using var tree = new TempTree();
        var held = Scenario.Create(tree);
        Directory.CreateDirectory(Path.GetDirectoryName(held.Dll)!);
        File.WriteAllBytes(held.Dll, new byte[] { 1, 2, 3, 4 });
        held.Runner.BuildTimedOut = true;

        var result = await held.Ensure.ExecuteAsync(held.Request());

        Assert.False(result.TestsStarted);
        Assert.Null(result.TestArguments);
        Assert.True(File.Exists(held.Dll));
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
        Assert.NotEqual(BuildFreshnessState.Current, held.State.GetState(held.ProjectId, held.Context));

        var later = held.Snapshot(held.Generation, inputRevision: 1, outputRevision: 0);
        held.State.Observe(held.Snapshot(Guid.NewGuid(), inputRevision: 1, outputRevision: 0));
        Assert.False(held.State.TryRecordBuildResult(
            held.ProjectId,
            held.Context,
            later,
            succeeded: true,
            cancelled: false,
            held.Dll));
        Assert.Null(held.State.GetProof(held.ProjectId, held.Context));
    }

    [Fact]
    public async Task Unknown_coverage_pending_and_metadata_only_stay_unusable_after_success()
    {
        using var tree = new TempTree();
        await AssertStaysUnusable(tree, coverage: InputCoverage.Unknown, pending: false, metadataGap: false, TestDllEnsure.ReasonCoverageUnknown);
        await AssertStaysUnusable(tree, coverage: InputCoverage.Complete, pending: true, metadataGap: false, TestDllEnsure.ReasonUnknown);
        await AssertStaysUnusable(tree, coverage: InputCoverage.Complete, pending: false, metadataGap: true, TestDllEnsure.ReasonCoverageUnknown);
    }

    [Fact]
    public async Task Referenced_project_stays_unconfirmed_when_the_target_build_exits_zero()
    {
        using var tree = new TempTree();
        var app = ProjectId.CreateNewId();
        var library = ProjectId.CreateNewId();
        var scenario = Scenario.Create(tree, app);
        scenario.LibraryId = library;
        var result = await scenario.Ensure.ExecuteAsync(scenario.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(0, result.BuildRun!.ExitCode);
        Assert.DoesNotContain("BuildProjectReferences", result.BuildArguments, StringComparison.Ordinal);
        Assert.NotNull(scenario.State.GetProof(app, scenario.Context));
        Assert.Null(scenario.State.GetProof(library, scenario.Context));
        Assert.NotEqual(BuildFreshnessState.Current, scenario.State.GetState(app, scenario.Context));
        Assert.NotEqual(BuildFreshnessState.Current, scenario.State.GetState(library, scenario.Context));
        Assert.False(scenario.State.EvaluateReuse(app, scenario.Context).SkipAllowed);
        Assert.False(scenario.State.EvaluateReuse(library, scenario.Context).SkipAllowed);
    }

    [Fact]
    public async Task Generated_output_revision_does_not_cause_a_rebuild()
    {
        using var tree = new TempTree();
        var scenario = Scenario.Create(tree);
        scenario.Runner.OnBuild = () =>
        {
            scenario.OutputRevision++;
            return Task.CompletedTask;
        };

        var result = await scenario.Ensure.ExecuteAsync(scenario.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(1, scenario.Runner.BuildCount);
        Assert.NotEqual(BuildFreshnessState.Dirty, scenario.State.GetState(scenario.ProjectId, scenario.Context));
        Assert.False(scenario.State.EvaluateReuse(scenario.ProjectId, scenario.Context).SkipAllowed);
    }

    [Fact]
    public async Task Roleless_generated_looking_input_is_not_dropped()
    {
        using var tree = new TempTree();
        var scenario = Scenario.Create(tree);
        scenario.Roles = Array.Empty<InputRole>();
        scenario.InputName = "GeneratedLooks.g.cs";
        scenario.Runner.OnBuild = () =>
        {
            if (scenario.Runner.BuildCount == 1)
            {
                scenario.InputRevision = 2;
            }

            return Task.CompletedTask;
        };

        var result = await scenario.Ensure.ExecuteAsync(scenario.Request());

        Assert.True(result.TestsStarted);
        Assert.Equal(2, scenario.Runner.BuildCount);
        Assert.Contains("--no-build", result.TestArguments, StringComparison.Ordinal);
        Assert.Equal(BuildFreshnessState.Unknown, scenario.State.GetState(scenario.ProjectId, scenario.Context));
        Assert.False(scenario.State.EvaluateReuse(scenario.ProjectId, scenario.Context).SkipAllowed);
    }

    [Fact]
    public void Wpf_temporary_project_does_not_dirty_and_a_roleless_name_is_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", Guid.NewGuid().ToString("N"));
        var projectFile = Path.Combine(root, "App.csproj");
        var temporary = Path.Combine(root, "App_k3_wpftmp.csproj");
        var generated = Path.Combine(root, "obj", "App.AssemblyInfo.cs");
        var user = Path.Combine(root, "GeneratedLooks.g.cs");
        var projectId = ProjectId.CreateNewId();
        var session = new WorkspaceInputSession(new[] { projectFile });
        var state = new SessionBuildState();
        var context = new BuildContext("Debug", "AnyCPU", "net10.0", null, Path.Combine(root, "App.dll"));

        Assert.True(WorkspaceDiskPathFilter.IsWpfTemporaryProject(temporary));
        var temporaryIsLoaded = false;
        foreach (var path in session.LoadedProjectPaths)
        {
            if (InputPathCanon.Comparer.Equals(path, Path.GetFullPath(temporary)))
            {
                temporaryIsLoaded = true;
            }
        }

        Assert.False(temporaryIsLoaded);
        if (WorkspaceDiskPathFilter.IsWpfTemporaryProject(temporary)
            && !session.LoadedProjectPaths.Contains(Path.GetFullPath(temporary)))
        {
            // The watcher drops this path before it becomes an input. No second name filter.
        }
        else
        {
            session.RecordDiskEvent(Notice(temporary, InputRole.EvaluationInput, projectId, countsAsInput: true));
        }

        session.RecordDiskEvent(Notice(generated, InputRole.Generated, projectId, countsAsInput: false));
        var firstGenerated = session.Pull();
        session.RecordDiskEvent(Notice(generated, InputRole.Generated, projectId, countsAsInput: false));
        var secondGenerated = session.Pull();
        state.Observe(firstGenerated);
        state.Observe(secondGenerated);

        Assert.Equal(0, session.InputRevisionForPath(temporary));
        Assert.Equal(0, session.InputRevisionForPath(generated));
        Assert.True(session.OutputRevisionForPath(generated) > 1);
        Assert.NotEqual(BuildFreshnessState.Dirty, state.GetState(projectId, context));

        Assert.False(WorkspaceDiskPathFilter.IsWpfTemporaryProject(user));
        session.RecordDiskEvent(Notice(user, InputRole.None, projectId, countsAsInput: true));
        var firstUser = session.Pull();
        state.Observe(firstUser);
        var kept = Revision(firstUser, user);
        Assert.True(kept > 0);
        session.RecordDiskEvent(Notice(user, InputRole.None, projectId, countsAsInput: true));
        var secondUser = session.Pull();
        state.Observe(secondUser);

        Assert.True(Revision(secondUser, user) > kept);
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(projectId, context));
        Assert.False(state.EvaluateReuse(projectId, context).SkipAllowed);
    }

    [Fact]
    public async Task Own_write_and_flush_do_not_drop_a_revision_the_pull_still_reports()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectFile = Path.Combine(root, "App.csproj");
            var source = Path.Combine(root, "Program.cs");
            var payload = "class App {}"u8.ToArray();
            await File.WriteAllBytesAsync(source, payload);
            var projectId = ProjectId.CreateNewId();
            var manager = SolutionManagerTestFactory.Create();
            var session = manager.StartDiskWatcherSessionForTests(new[] { projectFile });
            session.RecordDiskEvent(Notice(source, InputRole.UserInput, projectId, countsAsInput: true));
            var recorded = session.InputRevisionForPath(source);
            Assert.True(recorded > 0);
            Assert.True(session.BeginOwnWrite(source));
            Assert.True(session.CommitOwnWrite(source, payload, notifyEvenWhenUnchanged: false));
            await manager.EnsureDiskChangesAppliedAsync();

            var first = manager.PullInputSnapshot();
            var second = manager.PullInputSnapshot();
            Assert.Equal(recorded, Revision(first, source));
            Assert.Equal(recorded, Revision(second, source));
            Assert.Equal(first.Generation, second.Generation);

            var state = manager.SessionBuildState;
            state.Observe(first);
            state.Observe(second);
            Assert.Equal(recorded, state.ObservedInputRevision(source));

            var sln = Path.Combine(root, "App.sln");
            await File.WriteAllTextAsync(sln, SingleProjectSln);
            var dll = Path.Combine(root, "bin", "AppTests.dll");
            var runner = new CountingRunner();
            var ensure = new TestDllEnsure(state, runner.Run);
            var result = await ensure.ExecuteAsync(new TestDllEnsureRequest
            {
                ProjectId = projectId,
                Context = new BuildContext("Debug", null, "net10.0", null, dll),
                Policy = TestBuildPolicy.Auto,
                PullSnapshot = manager.PullInputSnapshot,
                SolutionPath = sln,
                ProjectNeedle = "AppTests",
                AssemblyPath = dll,
                AssemblyExists = _ => true,
                BuildWorkingDirectory = root,
                TestWorkingDirectory = root,
                Timeout = TimeSpan.FromSeconds(12),
            });

            var after = manager.PullInputSnapshot();
            Assert.True(result.TestsStarted);
            Assert.Equal(recorded, Revision(after, source));
            Assert.Equal(recorded, state.ObservedInputRevision(source));
            Assert.Equal(Revision(first, source), Revision(second, source));
            Assert.False(state.EvaluateReuse(projectId, new BuildContext("Debug", null, "net10.0", null, dll)).SkipAllowed);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Shared_output_path_stays_unknown_for_both_contexts()
    {
        using var tree = new TempTree();
        var scenario = Scenario.Create(tree);
        var release = new BuildContext("Release", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", scenario.Dll);
        Assert.True((await scenario.Ensure.ExecuteAsync(scenario.Request())).TestsStarted);
        Assert.True((await scenario.Ensure.ExecuteAsync(scenario.Request(release))).TestsStarted);

        Assert.True(scenario.State.GetProof(scenario.ProjectId, scenario.Context)!.OutputPathContested);
        Assert.True(scenario.State.GetProof(scenario.ProjectId, release)!.OutputPathContested);
        Assert.Contains(BuildReuseBlocker.OutputPathNotUnique, scenario.State.EvaluateReuse(scenario.ProjectId, scenario.Context).Blockers);
        Assert.False(scenario.State.EvaluateReuse(scenario.ProjectId, scenario.Context).SkipAllowed);
        Assert.False(scenario.State.EvaluateReuse(scenario.ProjectId, release).SkipAllowed);
        Assert.NotEqual(BuildFreshnessState.Current, scenario.State.GetState(scenario.ProjectId, scenario.Context));
        Assert.NotEqual(BuildFreshnessState.Current, scenario.State.GetState(scenario.ProjectId, release));
    }

    private const string SingleProjectSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTests", "AppTests.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    private static async Task AssertStaysUnusable(
        TempTree tree,
        InputCoverage coverage,
        bool pending,
        bool metadataGap,
        string reason)
    {
        var scenario = Scenario.Create(tree);
        scenario.Coverage = coverage;
        scenario.Pending = pending;
        scenario.MetadataGap = metadataGap;
        var result = await scenario.Ensure.ExecuteAsync(scenario.Request());
        var decision = scenario.State.EvaluateReuse(scenario.ProjectId, scenario.Context);

        Assert.True(result.TestsStarted);
        Assert.Equal(reason, result.Reason);
        Assert.False(decision.SkipAllowed);
        Assert.NotEqual(BuildFreshnessState.Current, decision.State);
        if (coverage != InputCoverage.Complete)
        {
            Assert.Contains(BuildReuseBlocker.CoverageUnknown, decision.Blockers);
        }

        if (pending)
        {
            Assert.Contains(BuildReuseBlocker.PendingInput, decision.Blockers);
        }

        if (metadataGap)
        {
            Assert.Contains(BuildReuseBlocker.MetadataOnlyDependency, decision.Blockers);
        }
    }

    private static DiskEventDecision Notice(string path, InputRole role, ProjectId projectId, bool countsAsInput)
    {
        var full = Path.GetFullPath(path);
        return new DiskEventDecision(
            full,
            provenIrrelevant: false,
            dirtyUserSource: false,
            compositionStale: false,
            graphFile: false,
            new[] { new InputPathNotice(full, role, projectId, countsAsInput) });
    }

    private static int Revision(WorkspaceInputSnapshot snapshot, string path)
    {
        foreach (var entry in snapshot.Paths)
        {
            if (InputPathCanon.Comparer.Equals(entry.CanonicalPath, path))
            {
                return entry.InputRevision;
            }
        }

        throw new InvalidOperationException("Path was not in the pull.");
    }

    private sealed class Scenario
    {
        private Scenario(
            SessionBuildState state,
            CountingRunner runner,
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
            InputRevision = 1;
            Roles = new[] { InputRole.UserInput };
            InputName = "Program.cs";
            Coverage = InputCoverage.Complete;
        }

        public SessionBuildState State { get; }

        public CountingRunner Runner { get; }

        public TestDllEnsure Ensure { get; }

        public ProjectId ProjectId { get; }

        public ProjectId? LibraryId { get; set; }

        public BuildContext Context { get; }

        public string SolutionPath { get; }

        public string Dll { get; }

        public string Root { get; }

        public Guid Generation { get; }

        public int InputRevision { get; set; }

        public int OutputRevision { get; set; }

        public InputRole[] Roles { get; set; }

        public string InputName { get; set; }

        public InputCoverage Coverage { get; set; }

        public bool Pending { get; set; }

        public bool MetadataGap { get; set; }

        public static Scenario Create(TempTree tree, ProjectId? projectId = null)
        {
            var sln = tree.Write("App.sln", SingleProjectSln);
            var dll = Path.Combine(tree.Root, "bin", "AppTests.dll");
            var id = projectId ?? ProjectId.CreateNewId();
            var context = new BuildContext("Sit-Debug", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);
            var state = new SessionBuildState();
            var runner = new CountingRunner();
            return new Scenario(state, runner, new TestDllEnsure(state, runner.Run), id, context, sln, dll, tree.Root);
        }

        public WorkspaceInputSnapshot Snapshot(Guid generation, int inputRevision, int outputRevision)
        {
            var directory = Path.Combine(Root, "src");
            var projectFile = Path.Combine(directory, "App.csproj");
            var library = LibraryId;
            var references = library is null ? Array.Empty<ProjectId>() : new[] { library };
            var projects = new List<LoadedProjectInstance>
            {
                new(ProjectId, projectFile, "AppTests", "AppTests", references, new[] { "net10.0" }),
            };
            var regions = new List<MembershipRegion>
            {
                new(directory, Recursive: true, ProjectId, "project-directory", CompletenessKnown: true),
            };
            var paths = new List<InputPathSnapshot>
            {
                new(
                    Path.Combine(directory, InputName),
                    inputRevision,
                    outputRevision,
                    present: true,
                    Pending,
                    new[] { ProjectId },
                    Roles,
                    Array.Empty<ProjectId>()),
            };
            if (library is not null)
            {
                var libraryDirectory = Path.Combine(Root, "lib");
                projects.Add(new LoadedProjectInstance(
                    library,
                    Path.Combine(libraryDirectory, "Lib.csproj"),
                    "Lib",
                    "Lib",
                    Array.Empty<ProjectId>(),
                    new[] { "net10.0" }));
                regions.Add(new MembershipRegion(libraryDirectory, Recursive: true, library, "project-directory", CompletenessKnown: true));
                paths.Add(new InputPathSnapshot(
                    Path.Combine(libraryDirectory, "Lib.cs"),
                    1,
                    outputRevision: 0,
                    present: true,
                    pending: false,
                    new[] { library },
                    new[] { InputRole.UserInput },
                    Array.Empty<ProjectId>()));
            }

            if (MetadataGap)
            {
                paths.Add(new InputPathSnapshot(
                    Path.Combine(directory, "Vendor.dll"),
                    1,
                    outputRevision: 0,
                    present: true,
                    pending: false,
                    new[] { ProjectId },
                    new[] { InputRole.MetadataDependency },
                    Array.Empty<ProjectId>()));
            }

            var gaps = new List<InputCoverageGap>();
            if (Coverage != InputCoverage.Complete)
            {
                gaps.Add(new InputCoverageGap(InputCoverageReason.LoadIncomplete, InputCoverageScope.WholeGraph));
            }

            if (MetadataGap)
            {
                gaps.Add(new InputCoverageGap(InputCoverageReason.MetadataOnlyDependency, InputCoverageScope.WholeGraph));
            }

            return new WorkspaceInputSnapshot(
                generation,
                snapshotRevision: inputRevision,
                membershipRevision: 1,
                new[] { projectFile },
                projects,
                regions,
                paths,
                Coverage,
                gaps,
                evidenceSource: "loaded-graph",
                Pending);
        }

        public TestDllEnsureRequest Request(BuildContext? context = null)
        {
            return new TestDllEnsureRequest
            {
                ProjectId = ProjectId,
                Context = context ?? Context,
                Policy = TestBuildPolicy.Auto,
                PullSnapshot = () => Snapshot(Generation, InputRevision, OutputRevision),
                SolutionPath = SolutionPath,
                ProjectNeedle = "AppTests",
                AssemblyPath = Dll,
                AssemblyExists = _ => true,
                BuildWorkingDirectory = Root,
                TestWorkingDirectory = Root,
                Timeout = TimeSpan.FromSeconds(12),
            };
        }
    }

    private sealed class CountingRunner
    {
        public int BuildCount { get; private set; }

        public bool BuildTimedOut { get; set; }

        public Func<Task>? OnBuild { get; set; }

        public async Task<DotNetCliRunner.RunResult> Run(
            string arguments,
            string? workingDirectory,
            TimeSpan? timeout,
            int? previousExit,
            CancellationToken cancellationToken)
        {
            if (!arguments.StartsWith("build ", StringComparison.Ordinal))
            {
                return new DotNetCliRunner.RunResult(0, "test-log", "test-meta", 0, 0);
            }

            BuildCount++;
            if (OnBuild is not null)
            {
                await OnBuild().ConfigureAwait(false);
            }

            return new DotNetCliRunner.RunResult(0, "build-log", "build-meta", 0, 0, BuildTimedOut);
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
