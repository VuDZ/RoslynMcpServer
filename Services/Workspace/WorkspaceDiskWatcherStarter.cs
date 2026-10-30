using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Starts one <see cref="FileSystemWatcher"/> per descriptor that an enabled recursive watcher does not already
/// cover. Recursion is taken from the descriptor. A non-recursive descriptor is never widened to an ancestor,
/// and it is not promoted to recursive when it shares a directory with a membership watcher. The file-name filter
/// stays <c>*.*</c> so a neighboring file in a point-watched directory is still delivered and classified by role.
/// </summary>
/// <remarks>
/// On Linux every watcher is a separate inotify instance, and <c>fs.inotify.max_user_instances</c> (128 by
/// default) is shared by all processes of the user. A point watcher on <c>bin</c>/<c>obj</c> inside a project
/// directory only duplicated the events of the recursive project watcher, and a nested project directory was
/// watched twice. See docs/disk-watcher-inotify/README.md (O-03): coverage is decided only by recursive watchers
/// that actually started, so a failed recursive watcher leaves its point descriptors in place.
/// </remarks>
internal static class WorkspaceDiskWatcherStarter
{
    /// <summary>Test seam: the next watcher that reaches construction throws before it is enabled.</summary>
    internal static bool FailNextStartForTests { get; set; }

    public static WorkspaceDiskWatcherStartResult Start(
        IReadOnlyList<InputWatcherDescriptor> descriptors,
        WorkspaceInputSession session,
        Action<FileSystemEventArgs> onChanged,
        Action<RenamedEventArgs> onRenamed,
        Action<ErrorEventArgs> onError,
        Action<LogLevel, Exception?, string> log)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(onRenamed);
        ArgumentNullException.ThrowIfNull(onError);
        ArgumentNullException.ThrowIfNull(log);

        var watchers = new List<FileSystemWatcher>();
        var subscriptions = new List<(string Directory, bool IncludeSubdirectories)>();
        var failures = new List<(string Directory, string Reason)>();
        var startedRecursive = new List<string>();

        // An ancestor is shorter than its descendants, so it starts first and covers them.
        var recursive = descriptors
            .Where(static descriptor => descriptor.IncludeSubdirectories)
            .OrderBy(static descriptor => descriptor.Directory.Length);
        var pointDescriptors = descriptors.Where(static descriptor => !descriptor.IncludeSubdirectories);
        foreach (var descriptor in recursive.Concat(pointDescriptors))
        {
            if (IsCovered(descriptor.Directory, startedRecursive))
            {
                continue;
            }

            var watcher = TryStart(descriptor, session, onChanged, onRenamed, onError, log, failures);
            if (watcher is null)
            {
                continue;
            }

            watchers.Add(watcher);
            subscriptions.Add((descriptor.Directory, descriptor.IncludeSubdirectories));
            if (descriptor.IncludeSubdirectories)
            {
                startedRecursive.Add(descriptor.Directory);
            }
        }

        return new WorkspaceDiskWatcherStartResult(watchers, subscriptions, failures);
    }

    private static bool IsCovered(string directory, IReadOnlyList<string> startedRecursive) =>
        startedRecursive.Any(root => IsCoveredBy(directory, root));

    private static bool IsCoveredBy(string directory, string root)
    {
        if (!WorkspaceDiskPathFilter.IsPathUnderDirectory(directory, root, InputPathCanon.Comparison))
        {
            return false;
        }

        // Recursive watchers do not traverse junctions or symlinks: lexical containment loses events.
        // See docs/disk-watcher-inotify/windows-review.md (F-WIN-001).
        try
        {
            var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            {
                if (InputPathCanon.Comparer.Equals(Path.TrimEndingDirectorySeparator(current.FullName), ancestor))
                {
                    return true;
                }

                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static FileSystemWatcher? TryStart(
        InputWatcherDescriptor descriptor,
        WorkspaceInputSession session,
        Action<FileSystemEventArgs> onChanged,
        Action<RenamedEventArgs> onRenamed,
        Action<ErrorEventArgs> onError,
        Action<LogLevel, Exception?, string> log,
        List<(string Directory, string Reason)> failures)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Directory) || !Directory.Exists(descriptor.Directory))
        {
            session.NoteCoverageUnknown(
                InputCoverageReason.WatcherDirectoryMissing,
                CoverageScopeFor(descriptor.Directory));
            log(
                LogLevel.Debug,
                null,
                "Disk watcher not started: workspace directory missing (" + descriptor.Directory + ").");
            return null;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(descriptor.Directory)
            {
                IncludeSubdirectories = descriptor.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.DirectoryName,
                Filter = "*.*",
            };

            if (OperatingSystem.IsWindows())
            {
                watcher.InternalBufferSize = 64 * 1024;
            }

            if (FailNextStartForTests)
            {
                FailNextStartForTests = false;
                throw new IOException("injected-watcher-start-failure");
            }

            watcher.Changed += (_, args) => onChanged(args);
            watcher.Created += (_, args) => onChanged(args);
            watcher.Deleted += (_, args) => onChanged(args);
            watcher.Renamed += (_, args) => onRenamed(args);
            watcher.Error += (_, args) => onError(args);
            watcher.EnableRaisingEvents = true;
            var started = watcher;
            watcher = null;
            log(LogLevel.Information, null, "Disk watcher started on " + descriptor.Directory);
            return started;
        }
        catch (Exception ex)
        {
            watcher?.Dispose();
            failures.Add((descriptor.Directory, ex.Message));
            session.NoteCoverageUnknown(
                InputCoverageReason.WatcherStartFailed,
                CoverageScopeFor(descriptor.Directory));
            log(
                LogLevel.Warning,
                ex,
                "Disk watcher failed to start for " + descriptor.Directory
                    + ". Symbol search stays on the load snapshot until reset_workspace.");
            return null;
        }
    }

    private static InputCoverageScope CoverageScopeFor(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return InputCoverageScope.WholeGraph;
        }

        var canonical = InputPathCanon.TryCanonicalize(directory);
        return canonical is null
            ? InputCoverageScope.WholeGraph
            : InputCoverageScope.ForPath(canonical);
    }
}
