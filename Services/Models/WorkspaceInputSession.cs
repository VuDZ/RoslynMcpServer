using System.Collections.Frozen;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Owns the watcher group's generation, loaded-project membership, and the evaluated input map.
/// A later re-evaluation of that map advances <see cref="MembershipRevision"/> on this same generation.
/// </summary>
internal sealed class WorkspaceInputSession
{
    public Guid Generation { get; } = Guid.NewGuid();

    public FrozenSet<string> LoadedProjectPaths { get; }

    /// <summary>Evaluated input map for <see cref="Generation"/>. Null until the loaded graph is published.</summary>
    public WorkspaceInputMap? InputMap { get; private set; }

    /// <summary>Advances when a map is published. This is not a second session generation.</summary>
    public int MembershipRevision { get; private set; }

    /// <summary>
    /// Runtime coverage of this generation. A directory rename or a watcher failure sets
    /// <see cref="InputCoverage.Unknown"/> and is not cleared by re-reading known sources.
    /// </summary>
    public InputCoverage ObservationCoverage { get; private set; } = InputCoverage.Unknown;

    public IReadOnlyList<string> ObservationUnknownReasons
    {
        get
        {
            lock (_gate)
            {
                return _observationUnknownReasons.ToArray();
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
            AbsorbPublishedCoverage(map);
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

    public void NoteCoverageUnknown(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            RememberUnknownReason(reason);
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
                RememberUnknownReason(InputCoverageReason.DirectoryRenameInsideMembership);
            }
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
        }
    }

    private void AbsorbPublishedCoverage(WorkspaceInputMap map)
    {
        foreach (var reason in map.UnknownReasons)
        {
            if (!_observationUnknownReasons.Contains(reason, StringComparer.Ordinal))
            {
                _observationUnknownReasons.Add(reason);
            }
        }

        if (map.WholeGraphCoverage == InputCoverage.Unknown || _observationUnknownReasons.Count > 0)
        {
            ObservationCoverage = InputCoverage.Unknown;
        }
        else
        {
            ObservationCoverage = InputCoverage.Complete;
        }
    }

    private void RememberUnknownReason(string reason)
    {
        ObservationCoverage = InputCoverage.Unknown;
        if (!_observationUnknownReasons.Contains(reason, StringComparer.Ordinal))
        {
            _observationUnknownReasons.Add(reason);
        }
    }

    private void RememberNotices(IReadOnlyList<InputPathNotice> notices)
    {
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

            if (notice.CountsAsInputRevision && notice.ProjectId is not null)
            {
                _inputRevisions.TryGetValue(notice.ProjectId, out var revision);
                _inputRevisions[notice.ProjectId] = revision + 1;
            }
        }
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

    private readonly object _gate = new();
    private bool _closed;
    private readonly List<string> _observationUnknownReasons = new();
    private readonly List<InputPathNotice> _notices = new();
    private readonly List<string> _compositionPaths = new();
    private readonly List<string> _directoryRenamePaths = new();
    private readonly Dictionary<ProjectId, int> _inputRevisions = new();
}
