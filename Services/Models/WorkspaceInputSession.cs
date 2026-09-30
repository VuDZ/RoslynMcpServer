using System.Collections.Frozen;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Owns the watcher group's generation and loaded-project membership. Future input revisions
/// extend this same session; this initial snapshot makes no claim about import coverage.
/// </summary>
internal sealed class WorkspaceInputSession
{
    public Guid Generation { get; } = Guid.NewGuid();

    public FrozenSet<string> LoadedProjectPaths { get; }

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
