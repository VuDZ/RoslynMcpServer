using System.Collections.Frozen;

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
        });
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

    private readonly object _gate = new();
    private bool _closed;
}
