using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class SessionBuildStateTests
{
    [Fact]
    public void Editing_dependency_dirties_consumers_and_building_one_does_not_confirm_the_other()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var baseline = Graph(generation, membershipRevision: 1, snapshotRevision: 1, app, other, dependency, dependencyRevision: 1);
        var edited = Graph(generation, membershipRevision: 1, snapshotRevision: 2, app, other, dependency, dependencyRevision: 2);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(baseline);
        state.Observe(edited);

        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(dependency, context));

        Assert.True(state.BeginBuild(app, context, edited));
        Assert.Equal(BuildFreshnessState.Building, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.True(state.TryRecordBuildResult(app, context, edited, succeeded: true, cancelled: false, "app-output"));

        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.Null(state.GetProof(other, context));
        var appProof = state.GetProof(app, context);
        Assert.NotNull(appProof);
        Assert.Equal("app-output", appProof.OutputIdentity);
    }

    [Fact]
    public void Incomplete_coverage_dirties_owners_and_does_not_walk_project_references()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(Graph(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            app,
            other,
            dependency,
            dependencyRevision: 1,
            coverage: InputCoverage.Unknown,
            gaps: new[] { Gap(InputCoverageReason.ImportsNotInEvaluatedGraph) }));
        state.Observe(Graph(
            generation,
            membershipRevision: 1,
            snapshotRevision: 2,
            app,
            other,
            dependency,
            dependencyRevision: 2,
            coverage: InputCoverage.Unknown,
            gaps: new[] { Gap(InputCoverageReason.ImportsNotInEvaluatedGraph) }));

        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(dependency, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(other, context));
    }

    [Fact]
    public void Shared_input_dirties_both_owners_and_building_one_does_not_clear_the_other()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var projects = new[] { Project(app, "App"), Project(other, "Other") };
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            projects,
            new[] { InputFile("Shared.cs", inputRevision: 1, new[] { app, other }) }));
        state.Observe(Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 2,
            projects,
            new[] { InputFile("Shared.cs", inputRevision: 2, new[] { app, other }) }));

        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.True(Record(state, app, context, Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 2,
            projects,
            new[] { InputFile("Shared.cs", inputRevision: 2, new[] { app, other }) })));

        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.Null(state.GetProof(other, context));
    }

    [Fact]
    public void Configuration_platform_tfm_and_build_args_are_distinct_keys()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var snapshot = SingleProject(generation, app, inputRevision: 1);
        var state = new SessionBuildState();
        state.Observe(snapshot);
        var debug = Context("Debug", outputName: "debug.dll");
        Assert.True(Record(state, app, debug, snapshot));
        Assert.NotNull(state.GetProof(app, debug));

        AssertIsolated(state, app, Context("Release", outputName: "release.dll"));
        AssertIsolated(state, app, Context("Debug", platform: "x64", outputName: "x64.dll"));
        AssertIsolated(state, app, Context("Debug", targetFramework: "net8.0", outputName: "net8.dll"));
        AssertIsolated(state, app, Context("Debug", buildArgs: "-p:Foo=1", outputName: "args.dll"));
    }

    [Fact]
    public void Missing_output_does_not_clear_dirty()
    {
        var (state, project, snapshot, context) = DirtyProject();
        Assert.True(state.BeginBuild(project, context, snapshot));
        Assert.False(state.TryRecordBuildResult(project, context, snapshot, succeeded: true, cancelled: false, outputIdentity: null));
        Assert.False(state.TryRecordBuildResult(project, context, snapshot, succeeded: true, cancelled: false, "  "));

        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(project, context));
        Assert.Null(state.GetProof(project, context));
    }

    [Fact]
    public void New_generation_drops_proofs_and_a_late_result_does_not_apply()
    {
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var oldGeneration = Guid.NewGuid();
        var edited = Graph(oldGeneration, 1, 2, app, other, dependency, dependencyRevision: 2);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(Graph(oldGeneration, 1, 1, app, other, dependency, dependencyRevision: 1));
        state.Observe(edited);
        Assert.True(Record(state, app, context, edited));
        Assert.NotNull(state.GetProof(app, context));

        state.Observe(Graph(Guid.NewGuid(), 1, 1, app, other, dependency, dependencyRevision: 1));

        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(other, context));
        Assert.Null(state.GetProof(app, context));
        Assert.False(state.TryRecordBuildResult(app, context, edited, succeeded: true, cancelled: false, "late-output"));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(dependency, context));
        Assert.Null(state.GetProof(app, context));
    }

    [Fact]
    public void Membership_revision_change_drops_unconfirmed_scope()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(Graph(generation, membershipRevision: 1, snapshotRevision: 1, app, other, dependency, dependencyRevision: 1));
        var edited = Graph(generation, membershipRevision: 1, snapshotRevision: 2, app, other, dependency, dependencyRevision: 2);
        state.Observe(edited);
        Assert.True(Record(state, dependency, context, edited));
        Assert.NotNull(state.GetProof(dependency, context));

        state.Observe(Graph(generation, membershipRevision: 2, snapshotRevision: 3, app, other, dependency, dependencyRevision: 2));

        Assert.Null(state.GetProof(dependency, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(dependency, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(dependency, context));
    }

    [Fact]
    public void Success_without_begin_build_after_generation_or_membership_change_does_not_store_a_proof()
    {
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var context = Context("Debug", outputName: "App.dll");

        var replaced = new SessionBuildState();
        var previous = Graph(Guid.NewGuid(), 1, 1, app, other, dependency, dependencyRevision: 1);
        replaced.Observe(previous);
        Assert.True(replaced.BeginBuild(app, context, previous));
        var fresh = Graph(Guid.NewGuid(), 1, 1, app, other, dependency, dependencyRevision: 1);
        replaced.Observe(fresh);
        Assert.False(replaced.TryRecordBuildResult(app, context, fresh, succeeded: true, cancelled: false, "late-output"));
        Assert.Null(replaced.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, replaced.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, replaced.GetState(other, context));
        Assert.Equal(BuildFreshnessState.Unknown, replaced.GetState(dependency, context));

        var generation = Guid.NewGuid();
        var remapped = new SessionBuildState();
        var first = Graph(generation, membershipRevision: 1, snapshotRevision: 1, app, other, dependency, dependencyRevision: 1);
        remapped.Observe(first);
        Assert.True(remapped.BeginBuild(app, context, first));
        var nextMembership = Graph(generation, membershipRevision: 2, snapshotRevision: 2, app, other, dependency, dependencyRevision: 1);
        remapped.Observe(nextMembership);
        Assert.False(remapped.TryRecordBuildResult(
            app,
            context,
            nextMembership,
            succeeded: true,
            cancelled: false,
            "late-output"));
        Assert.Null(remapped.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, remapped.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, remapped.GetState(other, context));
        Assert.Equal(BuildFreshnessState.Unknown, remapped.GetState(dependency, context));
    }

    [Fact]
    public void Complete_coverage_dirties_dependents_when_regions_are_unproven_and_a_framework_reference_is_present()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var projects = new[]
        {
            Project(app, "App", dependency),
            Project(other, "Other", dependency),
            Project(dependency, "Dependency"),
        };
        var regions = new[]
        {
            RegionFor(projects[0], known: false),
            RegionFor(projects[1], known: false),
            RegionFor(projects[2], known: false),
        };
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "FrameworkApp.dll");
        state.Observe(FrameworkGraph(generation, snapshotRevision: 1, dependencyRevision: 1, projects, regions, metadataGap: false));
        state.Observe(FrameworkGraph(generation, snapshotRevision: 2, dependencyRevision: 2, projects, regions, metadataGap: false));

        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(other, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(dependency, context));

        var withheld = new SessionBuildState();
        withheld.Observe(FrameworkGraph(generation, snapshotRevision: 1, dependencyRevision: 1, projects, regions, metadataGap: true));
        withheld.Observe(FrameworkGraph(generation, snapshotRevision: 2, dependencyRevision: 2, projects, regions, metadataGap: true));
        Assert.Equal(BuildFreshnessState.Dirty, withheld.GetState(dependency, context));
        Assert.Equal(BuildFreshnessState.Unknown, withheld.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, withheld.GetState(other, context));
    }

    [Fact]
    public void Dependency_edit_during_build_does_not_store_a_proof_when_coverage_is_complete()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var projects = RealPullProjects(app, other, dependency);
        var regions = UnprovenRegions(projects);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "DuringApp.dll");
        var started = FrameworkGraph(generation, snapshotRevision: 1, dependencyRevision: 1, projects, regions, metadataGap: false);
        var moved = FrameworkGraph(generation, snapshotRevision: 2, dependencyRevision: 2, projects, regions, metadataGap: false);
        state.Observe(started);
        Assert.True(state.BeginBuild(app, context, started));
        state.Observe(moved);

        Assert.False(state.TryRecordBuildResult(app, context, moved, succeeded: true, cancelled: false, "output"));
        Assert.Null(state.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
    }

    [Fact]
    public void Input_added_during_build_does_not_confirm_the_target_when_coverage_is_complete()
    {
        var generation = Guid.NewGuid();
        var project = ProjectId.CreateNewId();
        var projects = new[] { Project(project, "App") };
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "AddedDuring.dll");
        var started = Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            projects,
            new[] { InputFile("Program.cs", 1, new[] { project }) },
            coverage: InputCoverage.Complete,
            gaps: Array.Empty<InputCoverageGap>(),
            pending: false);
        state.Observe(started);
        Assert.True(state.BeginBuild(project, context, started));

        var during = Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 2,
            projects,
            new[]
            {
                InputFile("Program.cs", 1, new[] { project }),
                InputFile("Added.cs", 1, new[] { project }),
            },
            coverage: InputCoverage.Complete,
            gaps: Array.Empty<InputCoverageGap>(),
            pending: false);
        state.Observe(during);

        Assert.False(state.TryRecordBuildResult(project, context, during, succeeded: true, cancelled: false, "output"));
        Assert.Null(state.GetProof(project, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(project, context));
    }

    [Fact]
    public void Successful_build_clears_dirty_for_the_target_only_and_does_not_become_current()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var other = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var projects = RealPullProjects(app, other, dependency);
        var regions = UnprovenRegions(projects);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "BuiltApp.dll");
        state.Observe(FrameworkGraph(generation, snapshotRevision: 1, dependencyRevision: 1, projects, regions, metadataGap: false));
        var edited = FrameworkGraph(generation, snapshotRevision: 2, dependencyRevision: 2, projects, regions, metadataGap: false);
        state.Observe(edited);
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(dependency, context));

        Assert.True(state.BeginBuild(app, context, edited));
        Assert.True(state.TryRecordBuildResult(app, context, edited, succeeded: true, cancelled: false, "built-output"));

        Assert.NotEqual(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.False(state.EvaluateReuse(app, context).SkipAllowed);
        Assert.NotNull(state.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(dependency, context));
    }

    [Fact]
    public void Unproven_dependent_loses_its_proof_when_coverage_is_not_complete()
    {
        var generation = Guid.NewGuid();
        var consumer = ProjectId.CreateNewId();
        var library = ProjectId.CreateNewId();
        var projects = new[]
        {
            Project(consumer, "Consumer", library),
            Project(library, "Library"),
        };
        var regions = new[]
        {
            RegionFor(projects[0], known: false),
            RegionFor(projects[1], known: false),
        };
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "Consumer.dll");
        var baseline = UnprovenPull(generation, snapshotRevision: 1, libraryRevision: 1, projects, regions);
        state.Observe(baseline);
        Assert.True(state.BeginBuild(consumer, context, baseline));
        Assert.True(state.TryRecordBuildResult(consumer, context, baseline, succeeded: true, cancelled: false, "consumer-output"));
        Assert.NotNull(state.GetProof(consumer, context));

        state.Observe(UnprovenPull(generation, snapshotRevision: 2, libraryRevision: 2, projects, regions));

        Assert.Null(state.GetProof(consumer, context));
        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(consumer, context));
        Assert.NotEqual(BuildFreshnessState.Dirty, state.GetState(consumer, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(library, context));
    }

    [Fact]
    public void Other_project_edit_during_unconfirmed_build_does_not_confirm_the_target()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var neighbor = ProjectId.CreateNewId();
        var projects = new[]
        {
            Project(app, "RaceApp"),
            Project(neighbor, "RaceNeighbor"),
        };
        var regions = UnprovenRegions(projects);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "RaceApp.dll");
        state.Observe(RacePull(generation, snapshotRevision: 1, appRevision: 1, neighborRevision: 1, projects, regions));
        state.Observe(RacePull(generation, snapshotRevision: 2, appRevision: 2, neighborRevision: 1, projects, regions));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(neighbor, context));

        var started = RacePull(generation, snapshotRevision: 2, appRevision: 2, neighborRevision: 1, projects, regions);
        Assert.True(state.BeginBuild(app, context, started));
        var raced = RacePull(generation, snapshotRevision: 3, appRevision: 2, neighborRevision: 2, projects, regions);
        state.Observe(raced);

        Assert.False(state.TryRecordBuildResult(app, context, raced, succeeded: true, cancelled: false, "output"));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Null(state.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(neighbor, context));
    }

    [Fact]
    public void Failed_or_cancelled_build_does_not_clear_dirty()
    {
        var failed = DirtyProject();
        Assert.True(failed.State.BeginBuild(failed.Project, failed.Context, failed.Snapshot));
        Assert.False(failed.State.TryRecordBuildResult(
            failed.Project,
            failed.Context,
            failed.Snapshot,
            succeeded: false,
            cancelled: false,
            "output"));
        Assert.Equal(BuildFreshnessState.Dirty, failed.State.GetState(failed.Project, failed.Context));
        Assert.Null(failed.State.GetProof(failed.Project, failed.Context));

        var cancelled = DirtyProject();
        Assert.True(cancelled.State.BeginBuild(cancelled.Project, cancelled.Context, cancelled.Snapshot));
        Assert.False(cancelled.State.TryRecordBuildResult(
            cancelled.Project,
            cancelled.Context,
            cancelled.Snapshot,
            succeeded: true,
            cancelled: true,
            "output"));
        Assert.Equal(BuildFreshnessState.Dirty, cancelled.State.GetState(cancelled.Project, cancelled.Context));
        Assert.Null(cancelled.State.GetProof(cancelled.Project, cancelled.Context));
    }

    [Fact]
    public void Input_change_after_build_start_does_not_clear_dirty()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var started = SingleProject(generation, app, inputRevision: 2, snapshotRevision: 2);
        var during = SingleProject(generation, app, inputRevision: 3, snapshotRevision: 3);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(SingleProject(generation, app, inputRevision: 1, snapshotRevision: 1));
        state.Observe(started);
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.True(state.BeginBuild(app, context, started));
        state.Observe(during);

        Assert.False(state.TryRecordBuildResult(app, context, during, succeeded: true, cancelled: false, "output"));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(app, context));
        Assert.Null(state.GetProof(app, context));
    }

    [Fact]
    public void Xaml_or_import_revision_survives_creating_build_state_and_a_second_pull()
    {
        var projectId = ProjectId.CreateNewId();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState-" + Guid.NewGuid().ToString("N"));
        var xaml = Path.GetFullPath(Path.Combine(root, "MainWindow.xaml"));
        var import = Path.GetFullPath(Path.Combine(root, "Directory.Build.props"));
        var session = new WorkspaceInputSession(new[] { Path.Combine(root, "App.csproj") });
        session.RecordDiskEvent(Change(xaml, InputRole.AdditionalFile, projectId));
        session.RecordDiskEvent(Change(import, InputRole.EvaluationInput, projectId));
        var first = session.Pull();
        var xamlRevision = RevisionOf(first, xaml);
        var importRevision = RevisionOf(first, import);
        Assert.True(xamlRevision > 0);
        Assert.True(importRevision > 0);

        var state = new SessionBuildState();
        state.Observe(first);
        var second = session.Pull();
        state.Observe(second);

        Assert.Equal(xamlRevision, RevisionOf(second, xaml));
        Assert.Equal(importRevision, RevisionOf(second, import));
        Assert.Equal(xamlRevision, state.ObservedInputRevision(xaml));
        Assert.Equal(importRevision, state.ObservedInputRevision(import));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(projectId, Context("Debug", outputName: "App.dll")));
    }

    [Fact]
    public void Proof_stores_snapshot_generation_and_a_second_pull_returns_the_same_revisions()
    {
        var projectId = ProjectId.CreateNewId();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState-" + Guid.NewGuid().ToString("N"));
        var xaml = Path.GetFullPath(Path.Combine(root, "MainWindow.xaml"));
        var session = new WorkspaceInputSession(new[] { Path.Combine(root, "App.csproj") });
        session.RecordDiskEvent(Change(xaml, InputRole.AdditionalFile, projectId));
        var first = session.Pull();
        var again = session.Pull();
        var revision = RevisionOf(first, xaml);
        Assert.Equal(revision, RevisionOf(again, xaml));
        Assert.Equal(first.Generation, again.Generation);

        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(first);
        Assert.True(Record(state, projectId, context, first, "dll-bytes"));
        var proof = state.GetProof(projectId, context);
        Assert.NotNull(proof);
        Assert.Equal(first.Generation, proof.Generation);
        Assert.Contains(
            proof.Cursor.PathRevisions,
            path => InputPathCanon.Comparer.Equals(path.CanonicalPath, xaml) && path.InputRevision == revision);

        var afterProof = session.Pull();
        Assert.Equal(revision, RevisionOf(afterProof, xaml));
        Assert.Equal(first.Generation, state.GetProof(projectId, context)!.Generation);
    }

    [Fact]
    public void Fallback_build_does_not_make_unknown_coverage_complete_or_current()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var snapshot = Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            new[] { Project(app, "App") },
            new[] { InputFile("Program.cs", 1, new[] { app }) },
            coverage: InputCoverage.Unknown,
            evidenceSource: null,
            gaps: new[] { Gap(InputCoverageReason.ImportsNotInEvaluatedGraph) });
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(snapshot);
        Assert.True(Record(state, app, context, snapshot));

        var proof = state.GetProof(app, context);
        Assert.NotNull(proof);
        Assert.Equal(InputCoverage.Unknown, proof.Cursor.Coverage);
        var decision = state.EvaluateReuse(app, context);
        Assert.NotEqual(BuildFreshnessState.Current, decision.State);
        Assert.False(decision.SkipAllowed);
        Assert.Contains(BuildReuseBlocker.CoverageUnknown, decision.Blockers);
    }

    [Fact]
    public void Unindexed_path_metadata_only_dependency_and_unknown_membership_do_not_yield_current()
    {
        var app = ProjectId.CreateNewId();
        var generation = Guid.NewGuid();
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState", "Outside.cs"));
        var indexed = SingleProject(generation, app, inputRevision: 1);
        var recorded = Recorded(indexed, app);
        var outsideDecision = recorded.State.EvaluateReuse(app, recorded.Context, outside);
        Assert.NotEqual(BuildFreshnessState.Current, outsideDecision.State);
        Assert.False(outsideDecision.SkipAllowed);
        Assert.Contains(BuildReuseBlocker.PathNotInIndex, outsideDecision.Blockers);
        var knownPath = indexed.Paths[0].CanonicalPath;
        Assert.DoesNotContain(
            BuildReuseBlocker.PathNotInIndex,
            recorded.State.EvaluateReuse(app, recorded.Context, knownPath).Blockers);

        var metadata = Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            new[] { Project(app, "App") },
            new[]
            {
                InputFile("Program.cs", 1, new[] { app }),
                InputFile(
                    "vendor.dll",
                    inputRevision: 1,
                    new[] { app },
                    role: InputRole.MetadataDependency),
            });
        AssertBlocked(metadata, app, BuildReuseBlocker.MetadataOnlyDependency);

        var unknownMembership = Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision: 1,
            new[] { Project(app, "App") },
            new[] { InputFile("Program.cs", 1, new[] { app }) },
            regions: new[] { RegionFor(Project(app, "App"), known: false) });
        AssertBlocked(unknownMembership, app, BuildReuseBlocker.MembershipUnknown);
    }

    [Fact]
    public void Successful_build_with_complete_coverage_does_not_become_current_without_input_content_hashes()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var snapshot = SingleProject(generation, app, inputRevision: 1);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(snapshot);
        Assert.True(Record(state, app, context, snapshot, "dll-bytes"));

        var proof = state.GetProof(app, context);
        Assert.NotNull(proof);
        Assert.Equal(InputCoverage.Complete, proof.Cursor.Coverage);
        Assert.Empty(proof.Cursor.InputContentHashes);
        var decision = state.EvaluateReuse(app, context);
        Assert.Equal(BuildFreshnessState.Unknown, decision.State);
        Assert.False(decision.SkipAllowed);
        Assert.Contains(BuildReuseBlocker.InputContentHashAbsent, decision.Blockers);
        Assert.DoesNotContain(BuildReuseBlocker.CoverageUnknown, decision.Blockers);

        state.Observe(snapshot);
        Assert.NotNull(state.GetProof(app, context));
        Assert.Equal(snapshot.Generation, state.GetProof(app, context)!.Generation);
        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(app, context));
    }

    [Fact]
    public void Generated_output_revision_does_not_dirty_the_producer()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var projects = new[] { Project(app, "App") };
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(Snapshot(
            generation,
            1,
            1,
            projects,
            new[]
            {
                InputFile("Program.cs", 1, new[] { app }),
                InputFile("App.AssemblyInfo.cs", 0, new[] { app }, outputRevision: 1, role: InputRole.Generated, producers: new[] { app }),
            }));
        state.Observe(Snapshot(
            generation,
            1,
            2,
            projects,
            new[]
            {
                InputFile("Program.cs", 1, new[] { app }),
                InputFile("App.AssemblyInfo.cs", 0, new[] { app }, outputRevision: 2, role: InputRole.Generated, producers: new[] { app }),
            }));

        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
    }

    [Fact]
    public void Proven_unrelated_project_edit_does_not_dirty_the_selected_scope()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var dependency = ProjectId.CreateNewId();
        var unrelated = ProjectId.CreateNewId();
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(WithUnrelated(generation, snapshotRevision: 1, app, dependency, unrelated, unrelatedRevision: 1));
        state.Observe(WithUnrelated(generation, snapshotRevision: 2, app, dependency, unrelated, unrelatedRevision: 2));

        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(dependency, context));
        Assert.Equal(BuildFreshnessState.Dirty, state.GetState(unrelated, context));
    }

    [Fact]
    public void Pending_input_does_not_yield_current()
    {
        var generation = Guid.NewGuid();
        var app = ProjectId.CreateNewId();
        var snapshot = Snapshot(
            generation,
            1,
            1,
            new[] { Project(app, "App") },
            new[] { InputFile("Program.cs", 1, new[] { app }, pending: true) },
            pending: true);
        AssertBlocked(snapshot, app, BuildReuseBlocker.PendingInput);
    }

    [Fact]
    public void Command_name_is_not_build_evidence()
    {
        var app = ProjectId.CreateNewId();
        var snapshot = SingleProject(Guid.NewGuid(), app, inputRevision: 1);
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(snapshot);

        Assert.False(state.TryRecordInvocation("dotnet build"));
        Assert.False(state.TryRecordInvocation("execute_dotnet_command"));
        Assert.Null(state.GetProof(app, context));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, context));
    }

    [Fact]
    public void Shared_output_path_keeps_both_contexts_unknown()
    {
        var app = ProjectId.CreateNewId();
        var snapshot = SingleProject(Guid.NewGuid(), app, inputRevision: 1);
        var output = Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState", "App.dll");
        var debug = new BuildContext("Debug", "AnyCPU", "net10.0", buildArgs: null, output);
        var release = new BuildContext("Release", "AnyCPU", "net10.0", buildArgs: null, output);
        var state = new SessionBuildState();
        state.Observe(snapshot);
        Assert.True(Record(state, app, debug, snapshot, "debug-bytes"));
        Assert.True(Record(state, app, release, snapshot, "release-bytes"));

        Assert.True(state.GetProof(app, debug)!.OutputPathContested);
        Assert.True(state.GetProof(app, release)!.OutputPathContested);
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, debug));
        Assert.Equal(BuildFreshnessState.Unknown, state.GetState(app, release));
        Assert.Contains(BuildReuseBlocker.OutputPathNotUnique, state.EvaluateReuse(app, debug).Blockers);
        Assert.False(state.EvaluateReuse(app, release).SkipAllowed);
    }

    private static void AssertIsolated(SessionBuildState state, ProjectId project, BuildContext context)
    {
        Assert.Null(state.GetProof(project, context));
        Assert.NotEqual(BuildFreshnessState.Current, state.GetState(project, context));
    }

    private static void AssertBlocked(WorkspaceInputSnapshot snapshot, ProjectId project, string blocker)
    {
        var recorded = Recorded(snapshot, project);
        var decision = recorded.State.EvaluateReuse(project, recorded.Context);
        Assert.NotEqual(BuildFreshnessState.Current, decision.State);
        Assert.False(decision.SkipAllowed);
        Assert.Contains(blocker, decision.Blockers);
    }

    private static RecordedBuild Recorded(WorkspaceInputSnapshot snapshot, ProjectId project)
    {
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: project.Id + ".dll");
        state.Observe(snapshot);
        Assert.True(Record(state, project, context, snapshot));
        return new RecordedBuild(state, context);
    }

    private static bool Record(
        SessionBuildState state,
        ProjectId project,
        BuildContext context,
        WorkspaceInputSnapshot snapshot,
        string outputIdentity = "output-sha")
    {
        return state.BeginBuild(project, context, snapshot)
            && state.TryRecordBuildResult(project, context, snapshot, succeeded: true, cancelled: false, outputIdentity);
    }

    private static (SessionBuildState State, ProjectId Project, WorkspaceInputSnapshot Snapshot, BuildContext Context) DirtyProject()
    {
        var generation = Guid.NewGuid();
        var project = ProjectId.CreateNewId();
        var state = new SessionBuildState();
        var context = Context("Debug", outputName: "App.dll");
        state.Observe(SingleProject(generation, project, inputRevision: 1, snapshotRevision: 1));
        var edited = SingleProject(generation, project, inputRevision: 2, snapshotRevision: 2);
        state.Observe(edited);
        return (state, project, edited, context);
    }

    private static WorkspaceInputSnapshot SingleProject(
        Guid generation,
        ProjectId project,
        int inputRevision,
        int snapshotRevision = 1)
    {
        return Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision,
            new[] { Project(project, "App") },
            new[] { InputFile("Program.cs", inputRevision, new[] { project }) });
    }

    private static WorkspaceInputSnapshot Graph(
        Guid generation,
        int membershipRevision,
        int snapshotRevision,
        ProjectId app,
        ProjectId other,
        ProjectId dependency,
        int dependencyRevision,
        InputCoverage coverage = InputCoverage.Complete,
        IReadOnlyList<InputCoverageGap>? gaps = null)
    {
        return Snapshot(
            generation,
            membershipRevision,
            snapshotRevision,
            new[]
            {
                Project(app, "App", dependency),
                Project(other, "Other", dependency),
                Project(dependency, "Dependency"),
            },
            new[]
            {
                InputFile("App.cs", 1, new[] { app }),
                InputFile("Other.cs", 1, new[] { other }),
                InputFile("Dependency.cs", dependencyRevision, new[] { dependency }),
            },
            coverage,
            gaps: gaps);
    }

    private static LoadedProjectInstance[] RealPullProjects(ProjectId app, ProjectId other, ProjectId dependency)
    {
        return new[]
        {
            Project(app, "App", dependency),
            Project(other, "Other", dependency),
            Project(dependency, "Dependency"),
        };
    }

    private static MembershipRegion[] UnprovenRegions(IReadOnlyList<LoadedProjectInstance> projects)
    {
        var regions = new MembershipRegion[projects.Count];
        for (var i = 0; i < projects.Count; i++)
        {
            regions[i] = RegionFor(projects[i], known: false);
        }

        return regions;
    }

    private static WorkspaceInputSnapshot RacePull(
        Guid generation,
        int snapshotRevision,
        int appRevision,
        int neighborRevision,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<MembershipRegion> regions)
    {
        return Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision,
            projects,
            new[]
            {
                InputFile("RaceApp.cs", appRevision, new[] { projects[0].Id }),
                InputFile("RaceNeighbor.cs", neighborRevision, new[] { projects[1].Id }),
            },
            coverage: InputCoverage.Unknown,
            regions: regions);
    }

    private static WorkspaceInputSnapshot UnprovenPull(
        Guid generation,
        int snapshotRevision,
        int libraryRevision,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<MembershipRegion> regions)
    {
        return Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision,
            projects,
            new[]
            {
                InputFile("Consumer.cs", 1, new[] { projects[0].Id }),
                InputFile("Library.cs", libraryRevision, new[] { projects[1].Id }),
                InputFile("System.Runtime.dll", 1, new[] { projects[1].Id }, role: InputRole.MetadataDependency),
            },
            coverage: InputCoverage.Unknown,
            regions: regions);
    }

    private static WorkspaceInputSnapshot FrameworkGraph(
        Guid generation,
        int snapshotRevision,
        int dependencyRevision,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<MembershipRegion> regions,
        bool metadataGap)
    {
        var dependency = projects[2].Id;
        return Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision,
            projects,
            new[]
            {
                InputFile("FrameworkApp.cs", 1, new[] { projects[0].Id }),
                InputFile("FrameworkOther.cs", 1, new[] { projects[1].Id }),
                InputFile("FrameworkDependency.cs", dependencyRevision, new[] { dependency }),
                InputFile("System.Runtime.dll", 1, new[] { dependency }, role: InputRole.MetadataDependency),
            },
            coverage: InputCoverage.Complete,
            regions: regions,
            gaps: metadataGap ? new[] { Gap(InputCoverageReason.MetadataOnlyDependency) } : null);
    }

    private static WorkspaceInputSnapshot WithUnrelated(
        Guid generation,
        int snapshotRevision,
        ProjectId app,
        ProjectId dependency,
        ProjectId unrelated,
        int unrelatedRevision)
    {
        return Snapshot(
            generation,
            membershipRevision: 1,
            snapshotRevision,
            new[]
            {
                Project(app, "App", dependency),
                Project(dependency, "Dependency"),
                Project(unrelated, "Unrelated"),
            },
            new[]
            {
                InputFile("App.cs", 1, new[] { app }),
                InputFile("Dependency.cs", 1, new[] { dependency }),
                InputFile("Unrelated.cs", unrelatedRevision, new[] { unrelated }),
            });
    }

    private static WorkspaceInputSnapshot Snapshot(
        Guid generation,
        int membershipRevision,
        int snapshotRevision,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<InputPathSnapshot> paths,
        InputCoverage coverage = InputCoverage.Complete,
        string? evidenceSource = "loaded-graph",
        IReadOnlyList<MembershipRegion>? regions = null,
        IReadOnlyList<InputCoverageGap>? gaps = null,
        bool pending = false)
    {
        var resolvedRegions = regions ?? projects.Select(static project => RegionFor(project)).ToArray();
        var loadedPaths = new List<string>();
        foreach (var project in projects)
        {
            if (project.FilePath is not null)
            {
                loadedPaths.Add(project.FilePath);
            }
        }

        return new WorkspaceInputSnapshot(
            generation,
            snapshotRevision,
            membershipRevision,
            loadedPaths,
            projects,
            resolvedRegions,
            paths,
            coverage,
            gaps ?? Array.Empty<InputCoverageGap>(),
            evidenceSource,
            pending);
    }

    private static LoadedProjectInstance Project(ProjectId id, string name, params ProjectId[] references)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState", name);
        return new LoadedProjectInstance(
            id,
            Path.Combine(directory, name + ".csproj"),
            name,
            name,
            references,
            new[] { "net10.0" });
    }

    private static InputPathSnapshot InputFile(
        string fileName,
        int inputRevision,
        IReadOnlyList<ProjectId> owners,
        int outputRevision = 0,
        InputRole role = InputRole.UserInput,
        IReadOnlyList<ProjectId>? producers = null,
        bool pending = false)
    {
        return new InputPathSnapshot(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState", fileName)),
            inputRevision,
            outputRevision,
            present: true,
            pending,
            owners,
            new[] { role },
            producers ?? Array.Empty<ProjectId>());
    }

    private static MembershipRegion RegionFor(LoadedProjectInstance project, bool known = true)
    {
        var directory = Path.GetDirectoryName(project.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(directory));
        return new MembershipRegion(directory!, Recursive: true, project.Id, "project-directory", known);
    }

    private static InputCoverageGap Gap(string reason)
    {
        return new InputCoverageGap(reason, InputCoverageScope.WholeGraph);
    }

    private static BuildContext Context(
        string configuration,
        string platform = "AnyCPU",
        string targetFramework = "net10.0",
        string? buildArgs = null,
        string? outputName = "Debug/App.dll")
    {
        var output = outputName is null
            ? null
            : Path.Combine(Path.GetTempPath(), "RoslynMcpBuildState", outputName);
        return new BuildContext(configuration, platform, targetFramework, buildArgs, output);
    }

    private static DiskEventDecision Change(string path, InputRole role, ProjectId projectId)
    {
        return new DiskEventDecision(
            path,
            provenIrrelevant: false,
            dirtyUserSource: false,
            compositionStale: false,
            graphFile: false,
            new[] { new InputPathNotice(path, role, projectId, CountsAsInputRevision: true) });
    }

    private static int RevisionOf(WorkspaceInputSnapshot snapshot, string path)
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

    private sealed class RecordedBuild
    {
        public SessionBuildState State { get; }

        public BuildContext Context { get; }

        public RecordedBuild(SessionBuildState state, BuildContext context)
        {
            State = state;
            Context = context;
        }
    }
}
