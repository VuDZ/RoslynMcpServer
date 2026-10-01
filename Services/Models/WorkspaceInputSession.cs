using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Owns the watcher group's generation, loaded-project membership, input revisions, and own-write bytes.
/// A later re-evaluation of the map advances <see cref="MembershipRevision"/> on this same generation.
/// </summary>
internal sealed class WorkspaceInputSession
{
    public Guid Generation { get; } = Guid.NewGuid();

    public FrozenSet<string> LoadedProjectPaths { get; }

    /// <summary>Evaluated input map for <see cref="Generation"/>. Null until the loaded graph is published.</summary>
    public WorkspaceInputMap? InputMap { get; private set; }

    /// <summary>Advances when a map is published. This is not a second session generation.</summary>
    public int MembershipRevision { get; private set; }

    /// <summary>Advances when the pull state changes. This is not a second session generation.</summary>
    public int SnapshotRevision { get; private set; }

    /// <summary>
    /// Runtime coverage of this generation. A directory rename or a watcher failure sets
    /// <see cref="InputCoverage.Unknown"/> and is not cleared by re-reading known sources or by
    /// dropping queued events.
    /// </summary>
    public InputCoverage ObservationCoverage { get; private set; } = InputCoverage.Unknown;

    public IReadOnlyList<string> ObservationUnknownReasons
    {
        get
        {
            lock (_gate)
            {
                return _gaps.Select(static gap => gap.Reason).Distinct(StringComparer.Ordinal).ToArray();
            }
        }
    }

    public IReadOnlyList<InputPathNotice> Notices
    {
        get
        {
            lock (_gate)
            {
                return _notices.ToArray();
            }
        }
    }

    public IReadOnlyList<string> CompositionPaths
    {
        get
        {
            lock (_gate)
            {
                return _compositionPaths.ToArray();
            }
        }
    }

    public IReadOnlyList<string> DirectoryRenamePaths
    {
        get
        {
            lock (_gate)
            {
                return _directoryRenamePaths.ToArray();
            }
        }
    }

    public WorkspaceInputSession(IEnumerable<string?> projectFilePaths)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        foreach (var path in projectFilePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            try
            {
                paths.Add(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Invalid project paths cannot be a watcher root or an explicit-project exemption.
            }
        }
        LoadedProjectPaths = paths.ToFrozenSet(paths.Comparer);
    }

    /// <summary>
    /// Stores <paramref name="map"/> when its generation is this session's token.
    /// A foreign generation is rejected so the session does not adopt another counter.
    /// </summary>
    public bool TryPublishInputMap(WorkspaceInputMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Generation != Generation)
        {
            return false;
        }

        return TryRun(() =>
        {
            InputMap = map;
            MembershipRevision++;
            ReplaceMapGaps(map);
            Touch();
        });
    }

    /// <summary>
    /// Replaces the map after a new membership and import evaluation and restores coverage only
    /// when that evaluation finished and the required watchers are subscribed again.
    /// Re-reading known files is not this method. A failed or cancelled evaluation leaves gaps in place.
    /// Watcher errors and inaccessible inputs are not cleared here. An input-missing gap whose path
    /// exists as a directory is not a missing file and is cleared with the directory-rename gap.
    /// </summary>
    public bool TryAcceptMembershipEvaluation(WorkspaceInputMap? map, bool subscriptionsConfirmed)
    {
        if (map is null || map.Generation != Generation || !subscriptionsConfirmed || !map.LoadGraphComplete)
        {
            return false;
        }

        return TryRun(() =>
        {
            InputMap = map;
            MembershipRevision++;
            _gaps.RemoveAll(gap =>
                gap.Origin == GapOrigin.Runtime
                && (string.Equals(gap.Reason, InputCoverageReason.DirectoryRenameInsideMembership, StringComparison.Ordinal)
                    || IsExistingDirectoryMissing(gap)));
            ReplaceMapGaps(map);
            Touch();
        });
    }

    public int InputRevisionFor(ProjectId projectId)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        lock (_gate)
        {
            return _inputRevisions.TryGetValue(projectId, out var revision) ? revision : 0;
        }
    }

    public int InputRevisionForPath(string path)
    {
        lock (_gate)
        {
            var canonical = InputPathCanon.TryCanonicalize(path);
            if (canonical is null || !_paths.TryGetValue(canonical, out var state))
            {
                return 0;
            }

            return state.InputRevision;
        }
    }

    public int OutputRevisionForPath(string path)
    {
        lock (_gate)
        {
            var canonical = InputPathCanon.TryCanonicalize(path);
            if (canonical is null || !_paths.TryGetValue(canonical, out var state))
            {
                return 0;
            }

            return state.OutputRevision;
        }
    }

    public int EventRevision(string path)
    {
        lock (_gate)
        {
            var canonical = InputPathCanon.TryCanonicalize(path);
            if (canonical is null || !_paths.TryGetValue(canonical, out var state))
            {
                return 0;
            }

            return state.EventRevision;
        }
    }

    public bool IsPendingContent(string path)
    {
        lock (_gate)
        {
            var canonical = InputPathCanon.TryCanonicalize(path);
            return canonical is not null
                && _paths.TryGetValue(canonical, out var state)
                && state.PendingContent;
        }
    }

    public void NoteCoverageUnknown(string reason)
    {
        NoteCoverageUnknown(reason, InputCoverageScope.WholeGraph);
    }

    public void NoteCoverageUnknown(string reason, InputCoverageScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            RememberGap(reason, scope, GapOrigin.Runtime);
            Touch();
        }
    }

    public void RecordDiskEvent(DiskEventDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            if (decision.CompositionStale && decision.CanonicalPath is not null)
            {
                RememberPath(_compositionPaths, decision.CanonicalPath);
            }

            RememberNotices(decision.Notices);
            if (decision.CanonicalPath is not null)
            {
                var state = PathStateFor(decision.CanonicalPath);
                state.ContentRecordedEventRevision = state.EventRevision;
            }

            Touch();
        }
    }

    public void NoteDirectoryRename(string? oldPath, string? newPath, bool affectsMembership)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            RememberPath(_directoryRenamePaths, oldPath);
            RememberPath(_directoryRenamePaths, newPath);
            if (affectsMembership)
            {
                var scope = DirectoryRenameScope(oldPath, newPath);
                RememberGap(InputCoverageReason.DirectoryRenameInsideMembership, scope, GapOrigin.Runtime);
                Touch();
            }
        }
    }

    /// <summary>
    /// Registers a write before any bytes are persisted. An event that arrives while this is open
    /// stays pending; it is not an echo yet.
    /// </summary>
    public bool BeginOwnWrite(string path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            var state = PathStateFor(canonical);
            state.UncommittedWrite = true;
            state.EventRevisionAtUncommittedStart = state.EventRevision;
            Touch();
            return true;
        }
    }

    /// <summary>
    /// Stores the bytes that were actually persisted and, when requested, notifies every owner.
    /// The same payload committed again does not add another input revision.
    /// </summary>
    public bool CommitOwnWrite(string path, byte[] payload, bool notifyEvenWhenUnchanged)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            var state = PathStateFor(canonical);
            if (!state.UncommittedWrite)
            {
                return false;
            }

            var hadPayload = state.CommittedPayload is not null;
            var copy = payload.ToArray();
            var changed = !hadPayload || !state.CommittedPayload!.AsSpan().SequenceEqual(copy);
            state.CommittedPayload = copy;
            state.UncommittedWrite = false;
            state.ForceNotEcho = false;
            state.Present = true;
            // A repeat of the same bytes must not notify again. Arming an echo snapshot for a
            // change the watcher already recorded (no prior payload, notify not requested) must
            // not notify either. A different payload still does.
            var shouldNotify = notifyEvenWhenUnchanged
                ? changed || !state.NotifiedForCurrentPayload
                : hadPayload && changed;
            if (shouldNotify)
            {
                NotifyOwners(canonical);
                state.NotifiedForCurrentPayload = true;
                state.ContentRecordedEventRevision = state.EventRevision;
            }

            Touch();
            return true;
        }
    }

    /// <summary>Drops the open write. No echo bytes are stored for the attempt that failed.</summary>
    public void AbandonOwnWrite(string path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            if (!_paths.TryGetValue(canonical, out var state) || !state.UncommittedWrite)
            {
                return;
            }

            if (state.EventRevision > state.EventRevisionAtUncommittedStart)
            {
                state.ForceNotEcho = true;
            }

            state.UncommittedWrite = false;
            Touch();
        }
    }

    /// <summary>
    /// True while a content event must wait for a byte comparison: a write is still open,
    /// or this generation already stored the bytes of a successful write.
    /// </summary>
    public bool OwnWriteDefersContent(string path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_closed || !_paths.TryGetValue(canonical, out var state))
            {
                return false;
            }

            if (state.UncommittedWrite)
            {
                return true;
            }

            return state.CommittedPayload is not null && !state.ForceNotEcho;
        }
    }

    /// <summary>Queues a path and its event revision. Does not read the file.</summary>
    public void NotePendingContent(string path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            var state = PathStateFor(canonical);
            state.EventRevision++;
            state.PendingContent = true;
            Touch();
        }
    }

    public IReadOnlyList<string> CopyPendingContentPaths()
    {
        lock (_gate)
        {
            return _paths
                .Where(static pair => pair.Value.PendingContent)
                .Select(static pair => pair.Key)
                .ToArray();
        }
    }

    /// <summary>
    /// Applies a stable read that started at <paramref name="eventRevisionAtStart"/>.
    /// A newer event is left pending. Byte equality does not erase a revision already recorded,
    /// including a mismatch that later returned to the previous bytes.
    /// </summary>
    public ContentReconcileOutcome ApplyContentObservation(
        string path,
        StableFileRead read,
        int eventRevisionAtStart)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return ContentReconcileOutcome.None;
        }

        lock (_gate)
        {
            if (_closed || !_paths.TryGetValue(canonical, out var state) || !state.PendingContent)
            {
                return ContentReconcileOutcome.None;
            }

            if (state.EventRevision != eventRevisionAtStart)
            {
                return ContentReconcileOutcome.LeftPending;
            }

            if (state.UncommittedWrite)
            {
                return ContentReconcileOutcome.LeftPending;
            }

            if (read.Failed)
            {
                RememberGap(InputCoverageReason.InputInaccessible, InputCoverageScope.ForPath(canonical), GapOrigin.Runtime);
                Touch();
                return ContentReconcileOutcome.LeftPending;
            }

            if (read.Unstable)
            {
                RememberGap(InputCoverageReason.InputReadUnstable, InputCoverageScope.ForPath(canonical), GapOrigin.Runtime);
                Touch();
                return ContentReconcileOutcome.LeftPending;
            }

            if (read.IsDirectory)
            {
                state.PendingContent = false;
                Touch();
                return ContentReconcileOutcome.None;
            }

            if (read.Missing)
            {
                state.Present = false;
                RememberGap(InputCoverageReason.InputMissing, InputCoverageScope.ForPath(canonical), GapOrigin.Runtime);
                Touch();
                return ContentReconcileOutcome.LeftPending;
            }

            var echo = state.CommittedPayload is not null
                && !state.ForceNotEcho
                && read.Bytes is not null
                && state.CommittedPayload.AsSpan().SequenceEqual(read.Bytes);
            if (echo)
            {
                state.PendingContent = false;
                Touch();
                return ContentReconcileOutcome.Echo;
            }

            if (state.EventRevision > state.ContentRecordedEventRevision)
            {
                NotifyOwners(canonical);
                state.ContentRecordedEventRevision = state.EventRevision;
            }

            state.PendingContent = false;
            state.Present = true;
            Touch();
            return ContentReconcileOutcome.Changed;
        }
    }

    /// <summary>
    /// Drops pending flags only. Coverage gaps stay, including watcher overflow and directory rename.
    /// </summary>
    public void ClearPendingContent()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            foreach (var state in _paths.Values)
            {
                state.PendingContent = false;
            }

            Touch();
        }
    }

    /// <summary>
    /// Consistent view of this generation. Does not build, reload, or consume the changes.
    /// </summary>
    public WorkspaceInputSnapshot Pull()
    {
        lock (_gate)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>State changes and closure are serialized without acquiring the manager's workspace semaphore.</summary>
    public bool TryRun(Action update)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }
            update();
            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            foreach (var state in _paths.Values)
            {
                state.UncommittedWrite = false;
                state.CommittedPayload = null;
            }
        }
    }

    private void ReplaceMapGaps(WorkspaceInputMap map)
    {
        _gaps.RemoveAll(static gap => gap.Origin == GapOrigin.Map);
        // The evaluated map does not name a smaller scope for these reasons.
        foreach (var reason in map.UnknownReasons)
        {
            RememberGap(reason, InputCoverageScope.WholeGraph, GapOrigin.Map);
        }

        RecomputeCoverage(map);
    }

    private void RecomputeCoverage(WorkspaceInputMap? map)
    {
        if (_gaps.Count > 0 || map is null || map.WholeGraphCoverage == InputCoverage.Unknown || !map.PublishedAsComplete)
        {
            ObservationCoverage = InputCoverage.Unknown;
            return;
        }

        ObservationCoverage = InputCoverage.Complete;
    }

    private void RememberGap(string reason, InputCoverageScope scope, GapOrigin origin)
    {
        ObservationCoverage = InputCoverage.Unknown;
        if (_gaps.Exists(existing =>
                existing.Origin == origin
                && string.Equals(existing.Reason, reason, StringComparison.Ordinal)
                && SameScope(existing.Scope, scope)))
        {
            return;
        }

        _gaps.Add(new TrackedGap(reason, scope, origin));
    }

    private void RememberNotices(IReadOnlyList<InputPathNotice> notices)
    {
        var countedPaths = new HashSet<string>(InputPathCanon.Comparer);
        var outputPaths = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var notice in notices)
        {
            var index = _notices.FindIndex(existing =>
                InputPathCanon.Comparer.Equals(existing.CanonicalPath, notice.CanonicalPath)
                && existing.Role == notice.Role
                && existing.ProjectId == notice.ProjectId);
            if (index >= 0)
            {
                _notices[index] = notice;
            }
            else
            {
                _notices.Add(notice);
            }

            var state = PathStateFor(notice.CanonicalPath);
            if (notice.CountsAsInputRevision)
            {
                if (notice.ProjectId is not null)
                {
                    _inputRevisions.TryGetValue(notice.ProjectId, out var revision);
                    _inputRevisions[notice.ProjectId] = revision + 1;
                }
                else
                {
                    RememberGap(
                        InputCoverageReason.OwnersNotAttributed,
                        InputCoverageScope.ForPath(notice.CanonicalPath),
                        GapOrigin.Runtime);
                }

                if (countedPaths.Add(notice.CanonicalPath))
                {
                    state.InputRevision++;
                }
            }

            if (notice.Role is InputRole.Generated or InputRole.Output && outputPaths.Add(notice.CanonicalPath))
            {
                state.OutputRevision++;
            }

            if (notice.Role == InputRole.Unknown)
            {
                RememberGap(
                    InputCoverageReason.UnknownRoleOrProducer,
                    InputCoverageScope.ForPath(notice.CanonicalPath),
                    GapOrigin.Runtime);
            }
        }
    }

    private void NotifyOwners(string canonical)
    {
        if (InputMap is null)
        {
            var role = WorkspaceDiskPathFilter.IsCSharpSource(canonical)
                ? InputRole.UserInput
                : WorkspaceDiskPathFilter.IsProjectGraphFile(canonical)
                    ? InputRole.EvaluationInput
                    : InputRole.Unknown;
            RememberNotices(new[]
            {
                new InputPathNotice(canonical, role, ProjectId: null, role is InputRole.UserInput or InputRole.EvaluationInput),
            });
            return;
        }

        var decision = WorkspaceDiskEventClassifier.Classify(canonical, InputMap);
        if (decision.ProvenIrrelevant)
        {
            RememberGap(
                InputCoverageReason.OwnersNotAttributed,
                InputCoverageScope.ForPath(canonical),
                GapOrigin.Runtime);
            return;
        }

        RememberNotices(decision.Notices);
    }

    private WorkspaceInputSnapshot BuildSnapshot()
    {
        var map = InputMap;
        var seen = new HashSet<string>(InputPathCanon.Comparer);
        var paths = new List<InputPathSnapshot>();
        if (map is not null)
        {
            foreach (var entry in map.Paths)
            {
                seen.Add(entry.CanonicalPath);
                paths.Add(PathSnapshot(entry.CanonicalPath, entry));
            }
        }

        foreach (var pair in _paths)
        {
            if (!seen.Add(pair.Key))
            {
                continue;
            }

            paths.Add(PathSnapshot(pair.Key, map?.Find(pair.Key)));
        }

        paths.Sort(static (left, right) => InputPathCanon.Comparer.Compare(left.CanonicalPath, right.CanonicalPath));
        var gaps = _gaps
            .Select(static gap => new InputCoverageGap(gap.Reason, gap.Scope))
            .ToArray();
        return new WorkspaceInputSnapshot(
            Generation,
            SnapshotRevision,
            MembershipRevision,
            LoadedProjectPaths.ToArray(),
            map?.Projects ?? Array.Empty<LoadedProjectInstance>(),
            map?.Regions ?? Array.Empty<MembershipRegion>(),
            paths,
            ObservationCoverage,
            gaps,
            map is null ? null : WorkspaceInputMap.EvidenceSource,
            _paths.Values.Any(static state => state.PendingContent));
    }

    private InputPathSnapshot PathSnapshot(string canonical, InputPathEntry? entry)
    {
        _paths.TryGetValue(canonical, out var state);
        var owners = new List<ProjectId>();
        var roles = new List<InputRole>();
        var producers = new List<ProjectId>();
        if (entry is not null)
        {
            foreach (var occurrence in entry.Occurrences)
            {
                if (!owners.Contains(occurrence.ProjectId))
                {
                    owners.Add(occurrence.ProjectId);
                }

                if (!roles.Contains(occurrence.Role))
                {
                    roles.Add(occurrence.Role);
                }

                if (occurrence.ProducerProjectId is not null && !producers.Contains(occurrence.ProducerProjectId))
                {
                    producers.Add(occurrence.ProducerProjectId);
                }
            }
        }
        else
        {
            foreach (var notice in _notices)
            {
                if (!InputPathCanon.Comparer.Equals(notice.CanonicalPath, canonical))
                {
                    continue;
                }

                if (notice.ProjectId is not null && !owners.Contains(notice.ProjectId))
                {
                    owners.Add(notice.ProjectId);
                }

                if (!roles.Contains(notice.Role))
                {
                    roles.Add(notice.Role);
                }
            }
        }

        return new InputPathSnapshot(
            canonical,
            state?.InputRevision ?? 0,
            state?.OutputRevision ?? 0,
            state?.Present ?? entry?.Present ?? true,
            state?.PendingContent ?? false,
            owners,
            roles,
            producers);
    }

    private PathState PathStateFor(string canonical)
    {
        if (!_paths.TryGetValue(canonical, out var state))
        {
            state = new PathState();
            _paths.Add(canonical, state);
        }

        return state;
    }

    private static bool IsExistingDirectoryMissing(TrackedGap gap)
    {
        if (!string.Equals(gap.Reason, InputCoverageReason.InputMissing, StringComparison.Ordinal)
            || gap.Scope.CoversWholeGraph
            || gap.Scope.Paths.Count == 0)
        {
            return false;
        }

        foreach (var path in gap.Scope.Paths)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameScope(InputCoverageScope left, InputCoverageScope right)
    {
        if (left.CoversWholeGraph || right.CoversWholeGraph)
        {
            return left.CoversWholeGraph && right.CoversWholeGraph;
        }

        if (left.Paths.Count != right.Paths.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Paths.Count; i++)
        {
            if (!InputPathCanon.Comparer.Equals(left.Paths[i], right.Paths[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static InputCoverageScope DirectoryRenameScope(string? oldPath, string? newPath)
    {
        var paths = new List<string>();
        RememberPath(paths, oldPath);
        RememberPath(paths, newPath);
        if (paths.Count == 0)
        {
            return InputCoverageScope.WholeGraph;
        }

        return new InputCoverageScope(coversWholeGraph: false, paths, Array.Empty<ProjectId>());
    }

    private static void RememberPath(List<string> paths, string? rawPath)
    {
        var canonical = InputPathCanon.TryCanonicalize(rawPath);
        if (canonical is null)
        {
            return;
        }

        if (paths.Exists(existing => InputPathCanon.Comparer.Equals(existing, canonical)))
        {
            return;
        }

        paths.Add(canonical);
    }

    private void Touch()
    {
        SnapshotRevision++;
        RecomputeCoverage(InputMap);
    }

    private enum GapOrigin
    {
        Map = 0,
        Runtime,
    }

    private sealed class TrackedGap
    {
        public string Reason { get; }

        public InputCoverageScope Scope { get; }

        public GapOrigin Origin { get; }

        public TrackedGap(string reason, InputCoverageScope scope, GapOrigin origin)
        {
            Reason = reason;
            Scope = scope;
            Origin = origin;
        }
    }

    private sealed class PathState
    {
        public int InputRevision { get; set; }

        public int OutputRevision { get; set; }

        public int EventRevision { get; set; }

        public int ContentRecordedEventRevision { get; set; }

        public int EventRevisionAtUncommittedStart { get; set; }

        public bool PendingContent { get; set; }

        public bool Present { get; set; } = true;

        public bool UncommittedWrite { get; set; }

        public bool ForceNotEcho { get; set; }

        public bool NotifiedForCurrentPayload { get; set; }

        public byte[]? CommittedPayload { get; set; }
    }

    private readonly object _gate = new();
    private bool _closed;
    private readonly List<TrackedGap> _gaps = new();
    private readonly List<InputPathNotice> _notices = new();
    private readonly List<string> _compositionPaths = new();
    private readonly List<string> _directoryRenamePaths = new();
    private readonly Dictionary<ProjectId, int> _inputRevisions = new();
    private readonly Dictionary<string, PathState> _paths = new(InputPathCanon.Comparer);
}
