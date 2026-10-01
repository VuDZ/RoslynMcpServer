using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

/// <summary>
/// Build-freshness pull, own-write bytes, and coverage recovery.
/// A matching byte read is not proof that the watcher delivered every intermediate change.
/// </summary>
public sealed class WorkspaceInputFreshnessTests
{
    [Fact]
    public void External_edit_after_own_write_stays_dirty_inside_the_old_tick_window()
    {
        using var fixture = FreshFixture.Create();
        var payloadA = Encoding.UTF8.GetBytes("class A {}");
        var payloadB = Encoding.UTF8.GetBytes("class B {}");
        File.WriteAllBytes(fixture.SourcePath, payloadA);
        Assert.True(fixture.Session.BeginOwnWrite(fixture.SourcePath));
        Assert.True(fixture.Session.CommitOwnWrite(fixture.SourcePath, payloadA, notifyEvenWhenUnchanged: true));
        var afterOwnWrite = fixture.Session.InputRevisionForPath(fixture.SourcePath);

        File.WriteAllBytes(fixture.SourcePath, payloadB);
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.SourcePath));
        fixture.Manager.ReconcileInputEvents();

        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > afterOwnWrite);
        Assert.Contains(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.SourcePath));
        var pull = fixture.Manager.PullInputSnapshot();
        Assert.Contains(pull.Paths, path => Same(path.CanonicalPath, fixture.SourcePath) && path.InputRevision > afterOwnWrite);
    }

    [Fact]
    public void Partial_write_records_only_the_saved_path()
    {
        using var fixture = FreshFixture.Create();
        var saved = Encoding.UTF8.GetBytes("class Saved {}");
        var other = Encoding.UTF8.GetBytes("class Other {}");
        File.WriteAllBytes(fixture.SourcePath, saved);
        File.WriteAllBytes(fixture.OtherPath, other);

        Assert.True(fixture.Session.BeginOwnWrite(fixture.SourcePath));
        Assert.True(fixture.Session.BeginOwnWrite(fixture.OtherPath));
        Assert.True(fixture.Session.CommitOwnWrite(fixture.SourcePath, saved, notifyEvenWhenUnchanged: true));
        fixture.Session.NotePendingContent(fixture.OtherPath);
        fixture.Session.AbandonOwnWrite(fixture.OtherPath);
        var outcome = fixture.Session.ApplyContentObservation(
            fixture.OtherPath,
            StableFileRead.OfBytes(other),
            fixture.Session.EventRevision(fixture.OtherPath));

        Assert.Equal(ContentReconcileOutcome.Changed, outcome);
        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > 0);
        Assert.True(fixture.Session.InputRevisionForPath(fixture.OtherPath) > 0);
        Assert.False(fixture.Session.OwnWriteDefersContent(fixture.OtherPath));
    }

    [Fact]
    public void Matching_echo_does_not_add_another_revision_and_a_mismatch_does()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class Echo {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.CommitOwnWrite(fixture.SourcePath, payload, notifyEvenWhenUnchanged: true);
        var afterCommit = fixture.Session.InputRevisionForPath(fixture.SourcePath);

        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.SourcePath));
        fixture.Manager.ReconcileInputEvents();
        Assert.Equal(afterCommit, fixture.Session.InputRevisionForPath(fixture.SourcePath));
        Assert.DoesNotContain(fixture.Manager.GetPendingDirtySourcePaths(), path => Same(path, fixture.SourcePath));

        var changed = Encoding.UTF8.GetBytes("class External {}");
        File.WriteAllBytes(fixture.SourcePath, changed);
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.SourcePath));
        fixture.Manager.ReconcileInputEvents();
        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > afterCommit);
    }

    [Fact]
    public void Overflow_and_a_failed_watcher_stay_unknown_after_the_queue_is_cleared()
    {
        using var fixture = FreshFixture.Create();
        fixture.Manager.NotifyDiskWatcherError(fixture.Session, new InternalBufferOverflowException("overflow"));
        var pull = fixture.Manager.PullInputSnapshot();
        Assert.Equal(InputCoverage.Unknown, pull.Coverage);
        Assert.Contains(pull.Gaps, gap =>
            gap.Reason == InputCoverageReason.WatcherBufferOverflow && gap.Scope.CoversWholeGraph);

        fixture.Session.ClearPendingContent();
        pull = fixture.Manager.PullInputSnapshot();
        Assert.Equal(InputCoverage.Unknown, pull.Coverage);
        Assert.Contains(pull.Gaps, gap => gap.Reason == InputCoverageReason.WatcherBufferOverflow);

        var watched = Path.Combine(fixture.Root, "watched");
        Directory.CreateDirectory(watched);
        var session = new WorkspaceInputSession(Array.Empty<string>());
        WorkspaceDiskWatcherStarter.FailNextStartForTests = true;
        var started = WorkspaceDiskWatcherStarter.Start(
            new[]
            {
                new InputWatcherDescriptor(
                    watched,
                    IncludeSubdirectories: false,
                    Array.Empty<string>(),
                    new[] { InputRole.UserInput },
                    "point"),
                new InputWatcherDescriptor(
                    fixture.ProjectDirectory,
                    IncludeSubdirectories: true,
                    Array.Empty<string>(),
                    new[] { InputRole.UserInput },
                    "membership"),
            },
            session,
            onChanged: _ => { },
            onRenamed: _ => { },
            onError: _ => { },
            log: (_, _, _) => { });
        WorkspaceDiskWatcherStarter.FailNextStartForTests = false;
        try
        {
            Assert.NotEmpty(started.Watchers);
            Assert.Contains(InputCoverageReason.WatcherStartFailed, session.ObservationUnknownReasons);
        }
        finally
        {
            foreach (var watcher in started.Watchers)
            {
                watcher.Dispose();
            }
        }
        session.ClearPendingContent();
        Assert.Equal(InputCoverage.Unknown, session.Pull().Coverage);
        Assert.Contains(session.Pull().Gaps, gap => gap.Reason == InputCoverageReason.WatcherStartFailed);
    }

    [Fact]
    public async Task Change_during_flush_remains_in_the_snapshot()
    {
        using var fixture = FreshFixture.Create();
        fixture.Manager.DuringDiskFlushForTests = () =>
            fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.SourcePath));

        await fixture.Manager.EnsureDiskChangesAppliedAsync();

        var pull = fixture.Manager.PullInputSnapshot();
        Assert.Equal(fixture.Session.Generation, pull.Generation);
        Assert.Contains(pull.Paths, path => Same(path.CanonicalPath, fixture.SourcePath) && path.InputRevision > 0);
    }

    [Fact]
    public async Task Late_callback_from_the_old_generation_does_not_change_the_new_snapshot()
    {
        var manager = SolutionManagerTestFactory.Create();
        var oldSession = manager.StartDiskWatcherSessionForTests(Array.Empty<string>());
        var oldGeneration = oldSession.Generation;
        await manager.ClearWorkspaceAsync();
        var newSession = manager.StartDiskWatcherSessionForTests(Array.Empty<string>());
        var before = manager.PullInputSnapshot();

        manager.NotifyDiskWatcherChange(oldSession, Change(Path.Combine(Path.GetTempPath(), "Late.cs")));

        var after = manager.PullInputSnapshot();
        Assert.Equal(newSession.Generation, after.Generation);
        Assert.NotEqual(oldGeneration, after.Generation);
        Assert.Equal(before.SnapshotRevision, after.SnapshotRevision);
        Assert.Equal(before.Generation, after.Generation);
    }

    [Fact]
    public void Cancelled_evaluation_and_failed_write_do_not_become_an_echo()
    {
        using var fixture = FreshFixture.Create();
        fixture.Session.NoteDirectoryRename(fixture.ProjectDirectory, fixture.ProjectDirectory + "Next", affectsMembership: true);
        Assert.False(fixture.Session.TryAcceptMembershipEvaluation(null, subscriptionsConfirmed: false));
        Assert.False(fixture.Session.TryAcceptMembershipEvaluation(fixture.Map, subscriptionsConfirmed: false));
        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership, fixture.Session.ObservationUnknownReasons);

        var payload = Encoding.UTF8.GetBytes("class Kept {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.CommitOwnWrite(fixture.SourcePath, payload, notifyEvenWhenUnchanged: true);
        var afterCommit = fixture.Session.InputRevisionForPath(fixture.SourcePath);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.NotePendingContent(fixture.SourcePath);
        fixture.Session.AbandonOwnWrite(fixture.SourcePath);
        var outcome = fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.OfBytes(payload),
            fixture.Session.EventRevision(fixture.SourcePath));

        Assert.Equal(ContentReconcileOutcome.Changed, outcome);
        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > afterCommit);
    }

    [Fact]
    public async Task Non_csharp_notice_survives_semantic_flush_and_the_first_pull()
    {
        using var fixture = FreshFixture.Create();
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.XamlPath));
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.AdditionalPath));
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.ImportPath));

        var beforeFlush = fixture.Manager.PullInputSnapshot();
        await fixture.Manager.EnsureDiskChangesAppliedAsync();
        var afterFlush = fixture.Manager.PullInputSnapshot();

        Assert.Contains(afterFlush.Paths, path =>
            Same(path.CanonicalPath, fixture.XamlPath)
            && path.Roles.Contains(InputRole.AdditionalFile)
            && path.InputRevision > 0);
        Assert.Contains(afterFlush.Paths, path =>
            Same(path.CanonicalPath, fixture.AdditionalPath) && path.InputRevision > 0);
        Assert.Contains(afterFlush.Paths, path =>
            Same(path.CanonicalPath, fixture.ImportPath)
            && path.Roles.Contains(InputRole.EvaluationInput)
            && path.InputRevision > 0);
        Assert.True(afterFlush.Paths.Single(path => Same(path.CanonicalPath, fixture.XamlPath)).InputRevision
            >= beforeFlush.Paths.Single(path => Same(path.CanonicalPath, fixture.XamlPath)).InputRevision);
    }

    [Fact]
    public void Event_before_commit_is_not_an_echo_until_the_write_succeeds()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class Pending {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.NotePendingContent(fixture.SourcePath);
        var started = fixture.Session.EventRevision(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.LeftPending,
            fixture.Session.ApplyContentObservation(fixture.SourcePath, StableFileRead.OfBytes(payload), started));
        Assert.Equal(0, fixture.Session.InputRevisionForPath(fixture.SourcePath));
        Assert.True(fixture.Session.Pull().HasPendingContent);

        Assert.True(fixture.Session.CommitOwnWrite(fixture.SourcePath, payload, notifyEvenWhenUnchanged: true));
        var afterCommit = fixture.Session.InputRevisionForPath(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.Echo,
            fixture.Session.ApplyContentObservation(fixture.SourcePath, StableFileRead.OfBytes(payload), started));
        Assert.Equal(afterCommit, fixture.Session.InputRevisionForPath(fixture.SourcePath));
    }

    [Fact]
    public void Bom_only_difference_is_a_new_input_change()
    {
        using var fixture = FreshFixture.Create();
        var text = "class Bom {}"u8.ToArray();
        var withBom = new byte[text.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        text.CopyTo(withBom, 3);
        File.WriteAllBytes(fixture.SourcePath, text);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.CommitOwnWrite(fixture.SourcePath, text, notifyEvenWhenUnchanged: true);
        var afterCommit = fixture.Session.InputRevisionForPath(fixture.SourcePath);
        fixture.Session.NotePendingContent(fixture.SourcePath);

        var outcome = fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.OfBytes(withBom),
            fixture.Session.EventRevision(fixture.SourcePath));

        Assert.Equal(ContentReconcileOutcome.Changed, outcome);
        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > afterCommit);
    }

    [Fact]
    public void Missing_file_stays_pending()
    {
        using var fixture = FreshFixture.Create();
        fixture.Session.NotePendingContent(fixture.SourcePath);
        var outcome = fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.Absent,
            fixture.Session.EventRevision(fixture.SourcePath));

        Assert.Equal(ContentReconcileOutcome.LeftPending, outcome);
        Assert.True(fixture.Session.IsPendingContent(fixture.SourcePath));
        Assert.Contains(
            fixture.Session.Pull().Gaps,
            gap => gap.Reason == InputCoverageReason.InputMissing && !gap.Scope.CoversWholeGraph);
    }

    [Fact]
    public void Unstable_read_and_a_newer_event_stay_pending()
    {
        using var fixture = FreshFixture.Create();
        fixture.Session.NotePendingContent(fixture.SourcePath);
        var started = fixture.Session.EventRevision(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.LeftPending,
            fixture.Session.ApplyContentObservation(fixture.SourcePath, StableFileRead.NotStable, started));
        Assert.True(fixture.Session.IsPendingContent(fixture.SourcePath));
        Assert.Contains(
            fixture.Session.Pull().Gaps,
            gap => gap.Reason == InputCoverageReason.InputReadUnstable && !gap.Scope.CoversWholeGraph);

        fixture.Session.NotePendingContent(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.LeftPending,
            fixture.Session.ApplyContentObservation(
                fixture.SourcePath,
                StableFileRead.OfBytes("class Later {}"u8.ToArray()),
                started));
        Assert.True(fixture.Session.IsPendingContent(fixture.SourcePath));
    }

    [Fact]
    public void Detected_aba_sequence_keeps_the_revision()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class A {}");
        var other = Encoding.UTF8.GetBytes("class B {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.CommitOwnWrite(fixture.SourcePath, payload, notifyEvenWhenUnchanged: true);
        fixture.Session.NotePendingContent(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.Changed,
            fixture.Session.ApplyContentObservation(
                fixture.SourcePath,
                StableFileRead.OfBytes(other),
                fixture.Session.EventRevision(fixture.SourcePath)));
        var afterMismatch = fixture.Session.InputRevisionForPath(fixture.SourcePath);

        fixture.Session.NotePendingContent(fixture.SourcePath);
        Assert.Equal(
            ContentReconcileOutcome.Echo,
            fixture.Session.ApplyContentObservation(
                fixture.SourcePath,
                StableFileRead.OfBytes(payload),
                fixture.Session.EventRevision(fixture.SourcePath)));
        Assert.Equal(afterMismatch, fixture.Session.InputRevisionForPath(fixture.SourcePath));
        Assert.True(afterMismatch > 0);
    }

    [Fact]
    public void Delete_is_not_suppressed_by_matching_text()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class Gone {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.BeginOwnWrite(fixture.SourcePath);
        fixture.Session.CommitOwnWrite(fixture.SourcePath, payload, notifyEvenWhenUnchanged: true);
        var afterCommit = fixture.Session.InputRevisionForPath(fixture.SourcePath);
        File.Delete(fixture.SourcePath);

        fixture.Manager.NotifyDiskWatcherChange(
            fixture.Session,
            Change(fixture.SourcePath, WatcherChangeTypes.Deleted));

        Assert.True(fixture.Session.InputRevisionForPath(fixture.SourcePath) > afterCommit);
        Assert.Contains(fixture.Session.CompositionPaths, path => Same(path, fixture.SourcePath));
    }

    [Fact]
    public void Generated_output_does_not_increase_the_producer_input_revision()
    {
        using var fixture = FreshFixture.Create();
        var before = fixture.Session.InputRevisionFor(fixture.ProjectId);
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.OutputPath));
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.GeneratedPath));

        Assert.Equal(before, fixture.Session.InputRevisionFor(fixture.ProjectId));
        var pull = fixture.Manager.PullInputSnapshot();
        Assert.Contains(pull.Paths, path =>
            Same(path.CanonicalPath, fixture.OutputPath)
            && path.OutputRevision > 0
            && path.InputRevision == 0
            && path.Producers.Contains(fixture.ProjectId));
        Assert.Contains(pull.Paths, path =>
            Same(path.CanonicalPath, fixture.GeneratedPath) && path.OutputRevision > 0 && path.InputRevision == 0);
    }

    [Fact]
    public void Directory_rename_stays_unknown_until_membership_is_evaluated_again()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class Still {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        fixture.Session.NoteDirectoryRename(
            Path.Combine(fixture.ProjectDirectory, "Gone"),
            Path.Combine(fixture.ProjectDirectory, "Next"),
            affectsMembership: true);
        fixture.Session.NotePendingContent(fixture.SourcePath);
        fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.OfBytes(payload),
            fixture.Session.EventRevision(fixture.SourcePath));

        Assert.Equal(InputCoverage.Unknown, fixture.Session.Pull().Coverage);
        Assert.Contains(InputCoverageReason.DirectoryRenameInsideMembership, fixture.Session.ObservationUnknownReasons);

        Assert.False(fixture.Session.TryAcceptMembershipEvaluation(fixture.Map, subscriptionsConfirmed: false));
        Assert.Equal(InputCoverage.Unknown, fixture.Session.Pull().Coverage);

        Assert.True(fixture.Session.TryAcceptMembershipEvaluation(fixture.Map, subscriptionsConfirmed: true));
        var restored = fixture.Session.Pull();
        Assert.Equal(InputCoverage.Complete, restored.Coverage);
        Assert.DoesNotContain(
            restored.Gaps,
            gap => gap.Reason == InputCoverageReason.DirectoryRenameInsideMembership);
        Assert.Equal(fixture.Session.Generation, restored.Generation);
    }

    [Fact]
    public void Unconfirmed_target_framework_keeps_the_whole_graph_unknown()
    {
        using var fixture = FreshFixture.Create();
        var payload = Encoding.UTF8.GetBytes("class Inner {}");
        File.WriteAllBytes(fixture.SourcePath, payload);
        var incomplete = Map(
            fixture.Session.Generation,
            fixture,
            InputCoverage.Unknown,
            new[] { InputCoverageReason.UnconfirmedTargetFramework });
        Assert.True(fixture.Session.TryPublishInputMap(incomplete));
        fixture.Session.NotePendingContent(fixture.SourcePath);
        fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.OfBytes(payload),
            fixture.Session.EventRevision(fixture.SourcePath));
        Assert.True(fixture.Session.TryAcceptMembershipEvaluation(incomplete, subscriptionsConfirmed: true));

        var pull = fixture.Session.Pull();
        Assert.Equal(InputCoverage.Unknown, pull.Coverage);
        Assert.Contains(pull.Gaps, gap =>
            gap.Reason == InputCoverageReason.UnconfirmedTargetFramework && gap.Scope.CoversWholeGraph);
    }

    [Fact]
    public void Two_pulls_share_one_generation_and_one_read_does_not_clear_the_other()
    {
        using var fixture = FreshFixture.Create();
        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.SourcePath));
        var first = fixture.Manager.PullInputSnapshot();
        var second = fixture.Manager.PullInputSnapshot();

        Assert.Equal(fixture.Session.Generation, first.Generation);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(first.SnapshotRevision, second.SnapshotRevision);
        Assert.Equal(first.MembershipRevision, second.MembershipRevision);
        Assert.Equal(
            first.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).InputRevision,
            second.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).InputRevision);
        Assert.Contains(first.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).Owners, id => id == fixture.ProjectId);
        Assert.Contains(first.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).Owners, id => id == fixture.OtherProjectId);

        fixture.Manager.NotifyDiskWatcherChange(fixture.Session, Change(fixture.XamlPath));
        var third = fixture.Manager.PullInputSnapshot();
        Assert.Equal(
            first.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).InputRevision,
            second.Paths.Single(path => Same(path.CanonicalPath, fixture.SourcePath)).InputRevision);
        Assert.True(third.SnapshotRevision > first.SnapshotRevision);
        Assert.Equal(first.Generation, third.Generation);
        Assert.True(first.HasPendingContent);
    }

    [Fact]
    public async Task Pull_does_not_take_the_workspace_semaphore()
    {
        using var fixture = FreshFixture.Create();
        var semaphore = (SemaphoreSlim)typeof(SolutionManager)
            .GetField("_workspaceLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Manager)!;
        await semaphore.WaitAsync();
        try
        {
            var pull = fixture.Manager.PullInputSnapshot();
            Assert.Equal(fixture.Session.Generation, pull.Generation);
        }
        finally
        {
            semaphore.Release();
        }
    }

    [Fact]
    public void Inaccessible_read_keeps_unknown_for_that_path()
    {
        using var fixture = FreshFixture.Create();
        fixture.Session.NotePendingContent(fixture.SourcePath);
        var outcome = fixture.Session.ApplyContentObservation(
            fixture.SourcePath,
            StableFileRead.Unreadable,
            fixture.Session.EventRevision(fixture.SourcePath));

        Assert.Equal(ContentReconcileOutcome.LeftPending, outcome);
        var pull = fixture.Session.Pull();
        Assert.Equal(InputCoverage.Unknown, pull.Coverage);
        Assert.Contains(pull.Gaps, gap =>
            gap.Reason == InputCoverageReason.InputInaccessible
            && gap.Scope.Paths.Any(path => Same(path, fixture.SourcePath)));
        fixture.Session.ClearPendingContent();
        Assert.Contains(fixture.Session.Pull().Gaps, gap => gap.Reason == InputCoverageReason.InputInaccessible);
    }

    private static FileSystemEventArgs Change(string path, WatcherChangeTypes changeType = WatcherChangeTypes.Changed)
    {
        return new FileSystemEventArgs(changeType, Path.GetDirectoryName(path)!, Path.GetFileName(path));
    }

    private static bool Same(string? left, string? right)
    {
        return left is not null
            && right is not null
            && InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    }

    private static WorkspaceInputMap Map(
        Guid generation,
        FreshFixture fixture,
        InputCoverage wholeGraph,
        IReadOnlyList<string> unknownReasons)
    {
        var source = new InputPathEntry(fixture.SourcePath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.UserInput, ProducerProjectId: null, Present: true),
            new InputOccurrence(fixture.OtherProjectId, DocumentId: null, InputRole.UserInput, ProducerProjectId: null, Present: true),
        });
        var otherSource = new InputPathEntry(fixture.OtherPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.UserInput, ProducerProjectId: null, Present: true),
        });
        var xaml = new InputPathEntry(fixture.XamlPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.AdditionalFile, ProducerProjectId: null, Present: true),
        });
        var additional = new InputPathEntry(fixture.AdditionalPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.AdditionalFile, ProducerProjectId: null, Present: true),
        });
        var import = new InputPathEntry(fixture.ImportPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.EvaluationInput, ProducerProjectId: null, Present: true),
        });
        var generated = new InputPathEntry(fixture.GeneratedPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.Generated, fixture.ProjectId, Present: true),
        });
        var output = new InputPathEntry(fixture.OutputPath, new[]
        {
            new InputOccurrence(fixture.ProjectId, DocumentId: null, InputRole.Output, fixture.ProjectId, Present: true),
        });
        var project = new LoadedProjectInstance(
            fixture.ProjectId,
            fixture.ProjectFile,
            "App",
            "App",
            Array.Empty<ProjectId>(),
            new[] { "net10.0" });
        var other = new LoadedProjectInstance(
            fixture.OtherProjectId,
            fixture.OtherProjectFile,
            "Other",
            "Other",
            Array.Empty<ProjectId>(),
            new[] { "net10.0" });
        var region = new MembershipRegion(fixture.ProjectDirectory, Recursive: true, fixture.ProjectId, "project-directory", CompletenessKnown: true);
        var coverage = unknownReasons.Count == 0 ? InputCoverage.Complete : wholeGraph;
        return new WorkspaceInputMap(
            generation,
            loadGraphComplete: true,
            multiTargetCoverage: coverage,
            wholeGraphCoverage: coverage,
            unknownReasons,
            Array.Empty<string>(),
            new[] { project, other },
            new[] { source, otherSource, xaml, additional, import, generated, output },
            new[] { region },
            Array.Empty<InputWatcherDescriptor>(),
            Array.Empty<InputCategoryEvidence>(),
            new[] { fixture.ProjectDirectory });
    }

    private sealed class FreshFixture : IDisposable
    {
        private FreshFixture(
            string root,
            SolutionManager manager,
            WorkspaceInputSession session,
            WorkspaceInputMap map,
            ProjectId projectId,
            ProjectId otherProjectId)
        {
            Root = root;
            Manager = manager;
            Session = session;
            Map = map;
            ProjectId = projectId;
            OtherProjectId = otherProjectId;
        }

        public string Root { get; }

        public SolutionManager Manager { get; }

        public WorkspaceInputSession Session { get; }

        public WorkspaceInputMap Map { get; }

        public ProjectId ProjectId { get; }

        public ProjectId OtherProjectId { get; }

        public string ProjectDirectory => Path.Combine(Root, "App");

        public string ProjectFile => Path.Combine(ProjectDirectory, "App.csproj");

        public string OtherProjectFile => Path.Combine(Root, "Other", "Other.csproj");

        public string SourcePath => Path.Combine(ProjectDirectory, "Program.cs");

        public string OtherPath => Path.Combine(ProjectDirectory, "Other.cs");

        public string XamlPath => Path.Combine(ProjectDirectory, "MainWindow.xaml");

        public string AdditionalPath => Path.Combine(ProjectDirectory, "Notes.txt");

        public string ImportPath => Path.Combine(Root, "Directory.Build.props");

        public string GeneratedPath => Path.Combine(ProjectDirectory, "obj", "App.AssemblyInfo.cs");

        public string OutputPath => Path.Combine(ProjectDirectory, "bin", "App.dll");

        public static FreshFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpFresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "App", "obj"));
            Directory.CreateDirectory(Path.Combine(root, "App", "bin"));
            Directory.CreateDirectory(Path.Combine(root, "Other"));
            var projectId = ProjectId.CreateNewId();
            var otherProjectId = ProjectId.CreateNewId();
            var manager = SolutionManagerTestFactory.Create();
            var session = manager.StartDiskWatcherSessionForTests(new[]
            {
                Path.Combine(root, "App", "App.csproj"),
                Path.Combine(root, "Other", "Other.csproj"),
            });
            var fixture = new FreshFixture(root, manager, session, map: null!, projectId, otherProjectId);
            var map = WorkspaceInputFreshnessTests.Map(session.Generation, fixture, InputCoverage.Complete, Array.Empty<string>());
            Assert.True(session.TryPublishInputMap(map));
            Assert.Equal(InputCoverage.Complete, session.ObservationCoverage);
            return new FreshFixture(root, manager, session, map, projectId, otherProjectId);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
