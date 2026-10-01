using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Starts one <see cref="FileSystemWatcher"/> per descriptor. Recursion is taken from the descriptor.
/// A non-recursive descriptor is never widened to an ancestor, and it is not promoted to recursive
/// when it shares a directory with a membership watcher. The file-name filter stays <c>*.*</c> so a
/// neighboring file in a point-watched directory is still delivered and classified by role.
/// </summary>
internal static class WorkspaceDiskWatcherStarter
{
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

        var recursiveDirectories = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var descriptor in descriptors)
        {
            if (descriptor.IncludeSubdirectories)
            {
                recursiveDirectories.Add(descriptor.Directory);
            }
        }

        var watchers = new List<FileSystemWatcher>();
        var subscriptions = new List<(string Directory, bool IncludeSubdirectories)>();
        foreach (var descriptor in descriptors)
        {
            if (!descriptor.IncludeSubdirectories
                && recursiveDirectories.Contains(descriptor.Directory))
            {
                // The membership watcher on this same directory already delivers its files.
                // Skipping the duplicate does not watch a parent and does not make any other
                // non-recursive directory recursive.
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Directory) || !Directory.Exists(descriptor.Directory))
            {
                session.NoteCoverageUnknown(InputCoverageReason.WatcherDirectoryMissing);
                log(
                    LogLevel.Debug,
                    null,
                    "Disk watcher not started: workspace directory missing (" + descriptor.Directory + ").");
                continue;
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

                watcher.Changed += (_, args) => onChanged(args);
                watcher.Created += (_, args) => onChanged(args);
                watcher.Deleted += (_, args) => onChanged(args);
                watcher.Renamed += (_, args) => onRenamed(args);
                watcher.Error += (_, args) => onError(args);
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
                subscriptions.Add((descriptor.Directory, descriptor.IncludeSubdirectories));
                watcher = null;
                log(LogLevel.Information, null, "Disk watcher started on " + descriptor.Directory);
            }
            catch (Exception ex)
            {
                watcher?.Dispose();
                session.NoteCoverageUnknown(InputCoverageReason.WatcherStartFailed);
                log(
                    LogLevel.Warning,
                    ex,
                    "Disk watcher failed to start for " + descriptor.Directory
                        + ". Symbol search stays on the load snapshot until reset_workspace.");
            }
        }

        return new WorkspaceDiskWatcherStartResult(watchers, subscriptions);
    }
}
