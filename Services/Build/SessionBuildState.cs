using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// In-process freshness of project builds. Membership, owners, and revisions come from
/// a <see cref="WorkspaceInputSnapshot"/> pull. This type stores proofs and cursors only.
/// It does not watch the disk, hash inputs, or consume pull events.
/// A successful build is not reusable: the snapshot has no input content hash, so the
/// cursor cannot prove the bytes are unchanged.
/// </summary>
internal sealed class SessionBuildState
{
    public Guid? CurrentGeneration
    {
        get
        {
            lock (_mutation)
            {
                return _generation;
            }
        }
    }

    public BuildFreshnessState GetState(ProjectId projectId, BuildContext context)
    {
        return EvaluateReuse(projectId, context).State;
    }

    public BuildProof? GetProof(ProjectId projectId, BuildContext context)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        lock (_mutation)
        {
            return GetProofCore(projectId, context);
        }
    }

    private BuildProof? GetProofCore(ProjectId projectId, BuildContext context)
    {
        if (!_proofs.TryGetValue(new SlotKey(projectId, context), out var proof))
        {
            return null;
        }

        if (proof.OutputPathContested == OutputPathShared(context.OutputPath))
        {
            return proof;
        }

        return new BuildProof(
            proof.ProjectId,
            proof.Context,
            proof.Cursor,
            proof.OutputIdentity,
            OutputPathShared(context.OutputPath));
    }

    /// <summary>Last input revision observed for <paramref name="path"/>. Zero when the path was never in a pull.</summary>
    public int ObservedInputRevision(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_mutation)
        {
            var canonical = Canon(path);
            return _baseline.TryGetValue(canonical, out var revision) ? revision.InputRevision : 0;
        }
    }

    public BuildReuseDecision EvaluateReuse(ProjectId projectId, BuildContext context, string? candidatePath = null)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        lock (_mutation)
        {
            return EvaluateReuseCore(projectId, context, candidatePath);
        }
    }

    private BuildReuseDecision EvaluateReuseCore(ProjectId projectId, BuildContext context, string? candidatePath = null)
    {
        var blockers = new List<string>();
        var key = new SlotKey(projectId, context);
        if (_building.ContainsKey(key))
        {
            return new BuildReuseDecision(BuildFreshnessState.Building, skipAllowed: false, blockers);
        }

        if (_projects.TryGetValue(projectId, out var mark) && mark == ProjectMark.Dirty)
        {
            return new BuildReuseDecision(BuildFreshnessState.Dirty, skipAllowed: false, blockers);
        }

        if (!_proofs.TryGetValue(key, out var proof))
        {
            blockers.Add(BuildReuseBlocker.NoBuildProof);
            AddSnapshotBlockers(projectId, candidatePath, blockers);
            return new BuildReuseDecision(BuildFreshnessState.Unknown, skipAllowed: false, blockers);
        }

        AddProofBlockers(proof, projectId, candidatePath, blockers);
        if (blockers.Count == 0)
        {
            return new BuildReuseDecision(BuildFreshnessState.Current, skipAllowed: true, blockers);
        }

        return new BuildReuseDecision(BuildFreshnessState.Unknown, skipAllowed: false, blockers);
    }

    /// <summary>
    /// True when this project has a proof for a different configuration, platform, TFM,
    /// build arguments, or output path. That proof does not make this context current.
    /// </summary>
    public bool HasProofForDifferentContext(ProjectId projectId, BuildContext context)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        lock (_mutation)
        {
            return HasProofForDifferentContextCore(projectId, context);
        }
    }

    private bool HasProofForDifferentContextCore(ProjectId projectId, BuildContext context)
    {
        foreach (var pair in _proofs)
        {
            if (pair.Key.ProjectId == projectId && !pair.Key.Context.Equals(context))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies a pull. The same generation keeps proofs. A new generation, or a membership
    /// revision change, drops unconfirmed scope. Existing revisions are copied, not zeroed,
    /// and are not treated as a new edit on the first view of a generation.
    /// </summary>
    public void Observe(WorkspaceInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_mutation)
        {
            ObserveCore(snapshot);
        }
    }

    private void ObserveCore(WorkspaceInputSnapshot snapshot)
    {
        if (_generation is null || snapshot.Generation != _generation.Value)
        {
            Adopt(snapshot);
            return;
        }

        if (snapshot.MembershipRevision != _membershipRevision)
        {
            Adopt(snapshot);
            return;
        }

        ApplyEdits(snapshot);
    }

    /// <summary>
    /// Captures generation, context, and the input cursor, then marks the context building.
    /// The graph baseline is copied with that cursor. A later edit replaces the live baseline,
    /// and a path that was absent at start must still count as movement.
    /// A snapshot from another generation is ignored.
    /// </summary>
    public bool BeginBuild(ProjectId projectId, BuildContext context, WorkspaceInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_mutation)
        {
            return BeginBuildCore(projectId, context, snapshot);
        }
    }

    private bool BeginBuildCore(ProjectId projectId, BuildContext context, WorkspaceInputSnapshot snapshot)
    {
        if (_generation is not null && snapshot.Generation != _generation.Value)
        {
            return false;
        }

        ObserveCore(snapshot);
        if (_generation is null || snapshot.Generation != _generation.Value)
        {
            return false;
        }

        var key = new SlotKey(projectId, context);
        _building[key] = new InFlightBuild(CaptureCursor(snapshot, projectId), CopyBaseline());
        return true;
    }

    /// <summary>
    /// Records a finished build when <see cref="BeginBuild"/> captured a cursor for this
    /// generation and membership, and the target, context, pull evidence, and caller-supplied
    /// output identity are present. A success with no in-flight cursor is ignored, including
    /// after a new generation or membership revision dropped that cursor. Failure, cancellation,
    /// a missing output, and an input change since the start do not clear dirty and do not store a proof.
    /// Success clears dirty for the target only. It does not make the target current: the pull
    /// still has no input content hash, so reuse stays denied.
    /// </summary>
    public bool TryRecordBuildResult(
        ProjectId targetProjectId,
        BuildContext context,
        WorkspaceInputSnapshot evidence,
        bool succeeded,
        bool cancelled,
        string? outputIdentity)
    {
        ArgumentNullException.ThrowIfNull(targetProjectId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (_mutation)
        {
            return TryRecordBuildResultCore(targetProjectId, context, evidence, succeeded, cancelled, outputIdentity);
        }
    }

    private bool TryRecordBuildResultCore(
        ProjectId targetProjectId,
        BuildContext context,
        WorkspaceInputSnapshot evidence,
        bool succeeded,
        bool cancelled,
        string? outputIdentity)
    {
        if (_generation is null
            || evidence.Generation != _generation.Value
            || evidence.MembershipRevision != _membershipRevision)
        {
            return false;
        }

        var key = new SlotKey(targetProjectId, context);
        if (!_building.TryGetValue(key, out var inFlight))
        {
            return false;
        }

        _building.Remove(key);
        var started = inFlight.Cursor;
        var identity = string.IsNullOrWhiteSpace(outputIdentity) ? null : outputIdentity.Trim();
        if (!succeeded || cancelled || identity is null)
        {
            return false;
        }

        if (InputsMoved(started, evidence, targetProjectId, inFlight.StartBaseline)
            || (_latest is not null && InputsMoved(started, _latest, targetProjectId, inFlight.StartBaseline)))
        {
            NoteMoved(evidence, started, targetProjectId);
            return false;
        }

        _proofs[key] = new BuildProof(
            targetProjectId,
            context,
            started,
            identity,
            outputPathContested: false);
        _projects[targetProjectId] = ProjectMark.Unknown;
        return true;
    }

    /// <summary>
    /// A command name is not target, context, input evidence, or output identity.
    /// External builds and ad-hoc CLI invocations do not change freshness.
    /// </summary>
    public bool TryRecordInvocation(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return false;
    }

    /// <summary>
    /// Waits until this process can run one operation for the build context.
    /// The queue is inside this process. It does not stop, wait for, or describe
    /// a build that was started outside this process. A null budget waits without
    /// a deadline. <see cref="TimeSpan.Zero"/> does not wait.
    /// </summary>
    public async Task<ContextAdmission> EnterContextAsync(
        ProjectId projectId,
        BuildContext context,
        TimeSpan? waitBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        var slot = SlotFor(new SlotKey(projectId, context));
        if (slot.Gate.Wait(0))
        {
            return new ContextAdmission { Acquired = true };
        }

        if (waitBudget is { } exhausted && exhausted <= TimeSpan.Zero)
        {
            return new ContextAdmission();
        }

        bool acquired;
        if (waitBudget is null)
        {
            await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
        }
        else
        {
            acquired = await slot.Gate.WaitAsync(waitBudget.Value, cancellationToken).ConfigureAwait(false);
        }

        if (!acquired)
        {
            return new ContextAdmission();
        }

        var prior = slot.Last;
        return new ContextAdmission
        {
            Acquired = true,
            Waited = true,
            PriorFailed = prior.Failed,
            PriorConfirmed = prior.Confirmed,
            PriorGeneration = prior.Generation,
        };
    }

    /// <summary>
    /// Publishes the outcome of the operation that holds the context, then lets the next
    /// waiter in this process continue. A failed outcome is not that waiter's proof.
    /// </summary>
    public void LeaveContext(
        ProjectId projectId,
        BuildContext context,
        bool confirmed,
        bool failed,
        Guid generation)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        var slot = SlotFor(new SlotKey(projectId, context));
        slot.Last = new ContextOperationReport
        {
            Failed = failed,
            Confirmed = confirmed,
            Generation = generation,
        };
        slot.Gate.Release();
    }

    private void Adopt(WorkspaceInputSnapshot snapshot)
    {
        _generation = snapshot.Generation;
        _membershipRevision = snapshot.MembershipRevision;
        _projects.Clear();
        _proofs.Clear();
        _building.Clear();
        _latest = snapshot;
        foreach (var project in snapshot.Projects)
        {
            _projects[project.Id] = ProjectMark.Unknown;
        }

        ReplaceBaseline(snapshot);
    }

    private void ApplyEdits(WorkspaceInputSnapshot snapshot)
    {
        foreach (var path in snapshot.Paths)
        {
            // OutputRevision moves when a producing build rewrites a generated file.
            // That is not an input edit, so only InputRevision can dirty owners.
            var canonical = Canon(path.CanonicalPath);
            var previousInput = _baseline.TryGetValue(canonical, out var revision) ? revision.InputRevision : 0;
            if (path.InputRevision > previousInput)
            {
                NoteInputEdit(path, snapshot);
            }
        }

        _latest = snapshot;
        ReplaceBaseline(snapshot);
    }

    private void NoteInputEdit(InputPathSnapshot path, WorkspaceInputSnapshot snapshot)
    {
        foreach (var owner in path.Owners)
        {
            MarkDirty(owner);
        }

        // ProjectReferenceIds is the Roslyn reference list. Walk it only when this pull
        // says coverage is complete. A directory region stays CompletenessKnown false
        // because new glob items may appear; that is not incomplete coverage.
        // A producer-less metadata reference is a shared-framework or NuGet assembly and
        // does not withhold the list. A coverage gap, including metadata-only, does.
        if (!ReverseReferenceListIsConfirmed(snapshot))
        {
            // No other project is proven unrelated while the reference list is not a closure.
            // Drop stored proofs and in-flight cursors before the baseline advances, or the
            // edit is forgotten and the cursor is recorded as a confirmation.
            _proofs.Clear();
            _building.Clear();
            return;
        }

        foreach (var dependent in ReverseDependents(snapshot, path.Owners))
        {
            MarkDirty(dependent);
        }
    }

    private void NoteMoved(WorkspaceInputSnapshot snapshot, BuildInputCursor cursor, ProjectId target)
    {
        var scope = DependencyScope(snapshot, target);
        foreach (var path in snapshot.Paths)
        {
            var canonical = Canon(path.CanonicalPath);
            var inCursor = TryRevision(cursor, canonical, out var recorded);
            if (!inCursor && !Intersects(path, scope))
            {
                continue;
            }

            var baseline = inCursor ? recorded : 0;
            if (path.InputRevision > baseline)
            {
                NoteInputEdit(path, snapshot);
            }
        }
    }

    private void MarkDirty(ProjectId projectId)
    {
        _projects[projectId] = ProjectMark.Dirty;
        RemoveProofs(projectId);
    }

    private void RemoveProofs(ProjectId projectId)
    {
        var keys = new List<SlotKey>();
        foreach (var pair in _proofs)
        {
            if (pair.Key.ProjectId == projectId)
            {
                keys.Add(pair.Key);
            }
        }

        foreach (var key in keys)
        {
            _proofs.Remove(key);
        }
    }

    private BuildInputCursor CaptureCursor(WorkspaceInputSnapshot snapshot, ProjectId target)
    {
        var scope = DependencyScope(snapshot, target);
        var revisions = new List<BuildPathRevision>();
        var pending = snapshot.HasPendingContent;
        foreach (var path in snapshot.Paths)
        {
            if (!Intersects(path, scope))
            {
                continue;
            }

            if (path.Pending)
            {
                pending = true;
            }

            revisions.Add(new BuildPathRevision(Canon(path.CanonicalPath), path.InputRevision));
        }

        revisions.Sort(static (left, right) => InputPathCanon.Comparer.Compare(left.CanonicalPath, right.CanonicalPath));
        return new BuildInputCursor(
            snapshot.Generation,
            snapshot.MembershipRevision,
            snapshot.SnapshotRevision,
            snapshot.Coverage,
            pending,
            revisions);
    }

    private void AddProofBlockers(
        BuildProof proof,
        ProjectId projectId,
        string? candidatePath,
        List<string> blockers)
    {
        // Revisions identify an event. They are not a hash of the input bytes, and this
        // snapshot has nowhere to put such a hash. Reuse stays denied until the pull carries one.
        if (proof.Cursor.InputContentHashes.Count == 0
            || proof.Cursor.InputContentHashes.Count != proof.Cursor.PathRevisions.Count)
        {
            blockers.Add(BuildReuseBlocker.InputContentHashAbsent);
        }

        if (proof.Cursor.Coverage != InputCoverage.Complete
            || _latest is null
            || _latest.Coverage != InputCoverage.Complete)
        {
            blockers.Add(BuildReuseBlocker.CoverageUnknown);
        }

        if (OutputPathShared(proof.Context.OutputPath))
        {
            blockers.Add(BuildReuseBlocker.OutputPathNotUnique);
        }

        AddSnapshotBlockers(projectId, candidatePath, blockers);
    }

    private void AddSnapshotBlockers(ProjectId projectId, string? candidatePath, List<string> blockers)
    {
        if (_latest is null)
        {
            return;
        }

        if (_latest.EvidenceSource is null)
        {
            blockers.Add(BuildReuseBlocker.ProviderIncomplete);
        }

        if (_latest.Coverage != InputCoverage.Complete || _latest.Gaps.Count > 0)
        {
            AddOnce(blockers, BuildReuseBlocker.CoverageUnknown);
        }

        if (_latest.HasPendingContent || ScopePending(_latest, projectId))
        {
            blockers.Add(BuildReuseBlocker.PendingInput);
        }

        if (MembershipUnproven(_latest, projectId))
        {
            blockers.Add(BuildReuseBlocker.MembershipUnknown);
        }

        if (HasMetadataOnly(_latest, projectId))
        {
            blockers.Add(BuildReuseBlocker.MetadataOnlyDependency);
        }

        if (candidatePath is not null && !Indexed(candidatePath))
        {
            blockers.Add(BuildReuseBlocker.PathNotInIndex);
        }
    }

    private bool ScopePending(WorkspaceInputSnapshot snapshot, ProjectId projectId)
    {
        var scope = DependencyScope(snapshot, projectId);
        foreach (var path in snapshot.Paths)
        {
            if (path.Pending && Intersects(path, scope))
            {
                return true;
            }
        }

        return false;
    }

    private bool Indexed(string candidatePath)
    {
        if (_latest is null)
        {
            return false;
        }

        var canonical = Canon(candidatePath);
        foreach (var path in _latest.Paths)
        {
            if (InputPathCanon.Comparer.Equals(Canon(path.CanonicalPath), canonical))
            {
                return true;
            }
        }

        return false;
    }

    private bool OutputPathShared(string? outputPath)
    {
        if (outputPath is null)
        {
            return false;
        }

        var count = 0;
        foreach (var pair in _proofs)
        {
            if (InputPathCanon.Comparer.Equals(pair.Key.Context.OutputPath, outputPath))
            {
                count++;
            }
        }

        return count > 1;
    }

    private bool InputsMoved(
        BuildInputCursor cursor,
        WorkspaceInputSnapshot snapshot,
        ProjectId target,
        IReadOnlyDictionary<string, PathRevision> startBaseline)
    {
        // An unconfirmed reference list is not a way to decide which projects matter.
        // Any input-revision increase is movement. ProjectReferenceIds are walked only
        // when that list is confirmed.
        HashSet<ProjectId>? scope = ReverseReferenceListIsConfirmed(snapshot)
            ? DependencyScope(snapshot, target)
            : null;
        var seen = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var path in snapshot.Paths)
        {
            var canonical = Canon(path.CanonicalPath);
            if (scope is not null && !Intersects(path, scope))
            {
                continue;
            }

            seen.Add(canonical);
            if (!TryRevision(cursor, canonical, out var recorded))
            {
                // The live baseline is replaced as soon as an edit is observed, including
                // while this build is still in flight. Comparing a cursor miss with that
                // map treats a newly added input as unchanged, and a successful result
                // then clears Dirty. The copy from BeginBuild is the graph at start:
                // a path that was absent is revision 0, so a later non-zero revision is
                // movement. A path outside the captured scope that kept the same revision
                // is not movement; an unconfirmed reference list checks every path, while
                // the cursor stores only the target's dependency scope.
                recorded = startBaseline.TryGetValue(canonical, out var baseline)
                    ? baseline.InputRevision
                    : 0;
            }

            if (path.InputRevision != recorded)
            {
                return true;
            }
        }

        foreach (var recorded in cursor.PathRevisions)
        {
            if (!seen.Contains(Canon(recorded.CanonicalPath)))
            {
                return true;
            }
        }

        return false;
    }

    private Dictionary<string, PathRevision> CopyBaseline()
    {
        // ReplaceBaseline clears the live map. Each build needs the graph from the
        // moment it started, or a later edit hides a path that was not in the cursor.
        return new Dictionary<string, PathRevision>(_baseline, InputPathCanon.Comparer);
    }

    private void ReplaceBaseline(WorkspaceInputSnapshot snapshot)
    {
        _baseline.Clear();
        foreach (var path in snapshot.Paths)
        {
            _baseline[Canon(path.CanonicalPath)] = new PathRevision(path.InputRevision);
        }
    }

    private static bool ReverseReferenceListIsConfirmed(WorkspaceInputSnapshot snapshot)
    {
        return snapshot.Coverage == InputCoverage.Complete
            && !snapshot.HasPendingContent
            && snapshot.Gaps.Count == 0;
    }

    private static bool HasKnownRegion(WorkspaceInputSnapshot snapshot, ProjectId projectId)
    {
        foreach (var region in snapshot.Regions)
        {
            if (region.Owner == projectId && region.CompletenessKnown)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MembershipUnproven(WorkspaceInputSnapshot snapshot, ProjectId projectId)
    {
        if (snapshot.Regions.Count == 0)
        {
            return true;
        }

        var scope = DependencyScope(snapshot, projectId);
        foreach (var region in snapshot.Regions)
        {
            if (!region.CompletenessKnown && scope.Contains(region.Owner))
            {
                return true;
            }
        }

        foreach (var id in scope)
        {
            if (!HasKnownRegion(snapshot, id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMetadataOnly(WorkspaceInputSnapshot snapshot, ProjectId projectId)
    {
        var scope = DependencyScope(snapshot, projectId);
        foreach (var path in snapshot.Paths)
        {
            if (Intersects(path, scope) && IsMetadataOnly(path))
            {
                return true;
            }
        }

        foreach (var gap in snapshot.Gaps)
        {
            if (gap.Reason == InputCoverageReason.MetadataOnlyDependency && GapIntersects(gap, scope))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMetadataOnly(InputPathSnapshot path)
    {
        var metadata = false;
        var output = false;
        foreach (var role in path.Roles)
        {
            if (role == InputRole.MetadataDependency)
            {
                metadata = true;
            }

            if (role == InputRole.Output)
            {
                output = true;
            }
        }

        return metadata && !output && path.Producers.Count == 0;
    }

    private static bool GapIntersects(InputCoverageGap gap, HashSet<ProjectId> scope)
    {
        if (gap.Scope.CoversWholeGraph)
        {
            return true;
        }

        foreach (var owner in gap.Scope.Owners)
        {
            if (scope.Contains(owner))
            {
                return true;
            }
        }

        return gap.Scope.Owners.Count == 0 && gap.Scope.Paths.Count > 0;
    }

    private static HashSet<ProjectId> DependencyScope(WorkspaceInputSnapshot snapshot, ProjectId target)
    {
        var scope = new HashSet<ProjectId> { target };
        if (!ReverseReferenceListIsConfirmed(snapshot))
        {
            return scope;
        }

        var queue = new Queue<ProjectId>();
        queue.Enqueue(target);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var project in snapshot.Projects)
            {
                if (project.Id != id)
                {
                    continue;
                }

                foreach (var reference in project.ProjectReferenceIds)
                {
                    if (scope.Add(reference))
                    {
                        queue.Enqueue(reference);
                    }
                }
            }
        }

        return scope;
    }

    private static List<ProjectId> ReverseDependents(
        WorkspaceInputSnapshot snapshot,
        IReadOnlyList<ProjectId> roots)
    {
        var rootSet = new HashSet<ProjectId>(roots);
        var dependents = new HashSet<ProjectId>();
        var added = true;
        while (added)
        {
            added = false;
            foreach (var project in snapshot.Projects)
            {
                if (rootSet.Contains(project.Id) || dependents.Contains(project.Id))
                {
                    continue;
                }

                foreach (var reference in project.ProjectReferenceIds)
                {
                    if (rootSet.Contains(reference) || dependents.Contains(reference))
                    {
                        dependents.Add(project.Id);
                        added = true;
                        break;
                    }
                }
            }
        }

        return dependents.ToList();
    }

    private static bool Intersects(InputPathSnapshot path, HashSet<ProjectId> scope)
    {
        foreach (var owner in path.Owners)
        {
            if (scope.Contains(owner))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryRevision(BuildInputCursor cursor, string canonical, out int revision)
    {
        foreach (var path in cursor.PathRevisions)
        {
            if (InputPathCanon.Comparer.Equals(path.CanonicalPath, canonical))
            {
                revision = path.InputRevision;
                return true;
            }
        }

        revision = 0;
        return false;
    }

    private static void AddOnce(List<string> blockers, string blocker)
    {
        if (!blockers.Contains(blocker))
        {
            blockers.Add(blocker);
        }
    }

    private static string Canon(string path)
    {
        return InputPathCanon.TryCanonicalize(path) ?? path;
    }

    private ContextSlot SlotFor(SlotKey key)
    {
        lock (_contextSlotLookup)
        {
            if (!_contextSlots.TryGetValue(key, out var slot))
            {
                slot = new ContextSlot();
                _contextSlots.Add(key, slot);
            }

            return slot;
        }
    }

    private readonly record struct SlotKey(ProjectId ProjectId, BuildContext Context);

    private readonly record struct PathRevision(int InputRevision);

    /// <summary>
    /// Cursor for one in-flight build, plus the whole-graph baseline copied at its start.
    /// The baseline stays off <see cref="BuildInputCursor"/>: a proof keeps confirmed
    /// scope revisions, not every path in the graph.
    /// </summary>
    private readonly record struct InFlightBuild(
        BuildInputCursor Cursor,
        Dictionary<string, PathRevision> StartBaseline);

    private enum ProjectMark
    {
        Unknown = 0,
        Dirty,
    }

    private sealed class ContextSlot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public ContextOperationReport Last { get; set; }
    }

    private struct ContextOperationReport
    {
        public bool Failed { get; set; }

        public bool Confirmed { get; set; }

        public Guid Generation { get; set; }
    }

    private Guid? _generation;
    private int _membershipRevision;
    private WorkspaceInputSnapshot? _latest;
    private readonly Dictionary<ProjectId, ProjectMark> _projects = new();
    private readonly Dictionary<SlotKey, BuildProof> _proofs = new();

    // Dropped with the slot: Adopt and an unconfirmed edit clear this map, and
    // TryRecordBuildResult removes the entry. Two builds do not share a baseline.
    private readonly Dictionary<SlotKey, InFlightBuild> _building = new();
    private readonly Dictionary<string, PathRevision> _baseline = new(InputPathCanon.Comparer);

    // Serializes in-process mutations of the dictionaries above. Not held across a CLI process,
    // and not a gate on builds started outside this process.
    private readonly object _mutation = new();

    // One semaphore per build context so callers in this process do not overlap on that key.
    // The lookup lock is not held while a caller waits or while a CLI process runs.
    private readonly object _contextSlotLookup = new();
    private readonly Dictionary<SlotKey, ContextSlot> _contextSlots = new();
}
