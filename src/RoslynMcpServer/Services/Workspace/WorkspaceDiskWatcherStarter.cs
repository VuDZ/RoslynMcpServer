using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Starts one <see cref="FileSystemWatcher"/> per descriptor that an enabled recursive watcher does not already
/// cover. Recursion is taken from the descriptor. A non-recursive descriptor is never widened to an ancestor,
/// and it is not promoted to recursive when it shares a directory with a membership watcher. The file-name filter
/// stays <c>*.*</c> so a neighboring file in a point-watched directory is still delivered and classified by role.
/// Root lifetime subscriptions watch only directory names in the parent, never its descendant tree.
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
        var startedDescriptors = new List<InputWatcherDescriptor>();

        // An ancestor is shorter than its descendants, so it starts first and covers them.
        var recursive = descriptors
            .Where(static descriptor => descriptor.IncludeSubdirectories)
            .OrderBy(static descriptor => descriptor.Directory.Length);
        var pointDescriptors = descriptors.Where(static descriptor => !descriptor.IncludeSubdirectories);
        foreach (var descriptor in recursive.Concat(pointDescriptors))
        {
            if (startedDescriptors.Any(started => started.IncludeSubdirectories && IsCoveredBy(descriptor.Directory, started.Directory)))
            {
                continue;
            }

            var watcher = TryStart(descriptor.Directory, descriptor.IncludeSubdirectories, null, session, onChanged, onRenamed, onError, log, failures);
            if (watcher is null)
            {
                continue;
            }

            watchers.Add(watcher);
            subscriptions.Add((descriptor.Directory, descriptor.IncludeSubdirectories));
            startedDescriptors.Add(descriptor);
        }

        foreach (var (parent, childName) in RootLifetimeSubscriptions(startedDescriptors))
        {
            var watcher = TryStart(parent, false, childName, session, onChanged, onRenamed, onError, log, failures);
            if (watcher is not null)
            {
                watchers.Add(watcher);
                subscriptions.Add((parent, false));
            }
        }

        return new WorkspaceDiskWatcherStartResult(watchers, subscriptions, failures);
    }

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
        string directory,
        bool includeSubdirectories,
        string? rootFilter,
        WorkspaceInputSession session,
        Action<FileSystemEventArgs> onChanged,
        Action<RenamedEventArgs> onRenamed,
        Action<ErrorEventArgs> onError,
        Action<LogLevel, Exception?, string> log,
        List<(string Directory, string Reason)> failures)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            session.NoteCoverageUnknown(
                InputCoverageReason.WatcherDirectoryMissing,
                CoverageScopeFor(directory));
            log(
                LogLevel.Debug,
                null,
                "Disk watcher not started: workspace directory missing (" + directory + ").");
            return null;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = rootFilter is not null
                    ? NotifyFilters.DirectoryName
                    : NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                Filter = rootFilter ?? "*.*",
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

            if (rootFilter is null)
            {
                watcher.Changed += (_, args) => onChanged(args);
                watcher.Created += (_, args) => onChanged(args);
            }
            var guardedPath = rootFilter is null ? null : Path.Combine(directory, rootFilter);
            watcher.Deleted += (_, args) =>
            {
                if (guardedPath is null || InputPathCanon.Comparer.Equals(args.FullPath, guardedPath))
                {
                    onChanged(args);
                }
            };
            watcher.Renamed += (_, args) =>
            {
                if (guardedPath is null
                    || InputPathCanon.Comparer.Equals(args.OldFullPath, guardedPath)
                    || InputPathCanon.Comparer.Equals(args.FullPath, guardedPath))
                {
                    onRenamed(args);
                }
            };
            watcher.Error += (_, args) =>
            {
                var exception = args.GetException();
                var directoryExists = Directory.Exists(directory);
                var parent = Path.GetDirectoryName(directory);
                // Windows reports AccessDenied when a watched root is deleted, after its child Deleted events.
                // Journal mapped descendants through the same Deleted path without inventing an irreversible gap.
                // Overflow still means unidentified loss even if the root vanished; existing-root errors remain gaps.
                // See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-14-red.md (native root Error).
                if (exception is not InternalBufferOverflowException && !directoryExists && !string.IsNullOrEmpty(parent))
                {
                    onChanged(new FileSystemEventArgs(WatcherChangeTypes.Deleted, parent, Path.GetFileName(directory)));
                }
                else
                {
                    onError(args);
                }
                try
                {
                    log(
                        LogLevel.Debug,
                        exception,
                        $"Disk watcher error origin: {directory}; directoryExists={directoryExists}.");
                }
                catch (Exception)
                {
                    // Diagnostics must not let a logging provider escape a native callback after the event was admitted.
                }
            };
            watcher.EnableRaisingEvents = true;
            log(LogLevel.Information, null, "Disk watcher started on " + directory);
            return watcher;
        }
        catch (Exception ex)
        {
            watcher?.Dispose();
            failures.Add((directory, ex.Message));
            session.NoteCoverageUnknown(
                InputCoverageReason.WatcherStartFailed,
                CoverageScopeFor(directory));
            log(
                LogLevel.Warning,
                ex,
                "Disk watcher failed to start for " + directory
                    + ". Symbol search stays on the load snapshot until reset_workspace.");
            return null;
        }
    }

    private static IEnumerable<(string Parent, string ChildName)> RootLifetimeSubscriptions(
        IReadOnlyList<InputWatcherDescriptor> descriptors)
    {
        // Watching a directory's contents does not reliably report movement of the directory itself.
        // Guard its lifetime from the parent; continue until an existing subscription observes that parent too.
        // These exact-name, non-recursive guards do not widen membership or search roots.
        // See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-14.md (R14-001).
        var guardedDirectories = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var descriptor in descriptors)
        {
            // shortcut: Non-C# inputs do not independently seed lifetime guards; their content subscriptions remain.
            // Extend lifetime coverage when those inputs gain baseline coverage; SDK ancestor chains multiply OS watchers.
            // See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-14-red.md (resource correction).
            if (!NeedsRootLifetimeGuard(descriptor))
            {
                continue;
            }
            var directory = descriptor.Directory;
            if (!Directory.Exists(directory))
            {
                continue;
            }
            while (Directory.GetParent(directory) is { } parent && guardedDirectories.Add(directory))
            {
                var covering = descriptors.Where(candidate =>
                    InputPathCanon.Comparer.Equals(candidate.Directory, parent.FullName)
                    || candidate.IncludeSubdirectories && IsCoveredBy(parent.FullName, candidate.Directory)).ToArray();
                // A primary content subscription is not proof that its own root lifetime is protected.
                // Continue through excluded non-C# parents for the eligible descendant, without duplicate watches.
                if (covering.Any(NeedsRootLifetimeGuard))
                {
                    break;
                }
                if (covering.Length == 0)
                {
                    yield return (parent.FullName, Path.GetFileName(directory));
                }
                directory = parent.FullName;
            }
        }
    }

    private static bool NeedsRootLifetimeGuard(InputWatcherDescriptor descriptor) =>
        descriptor.IncludeSubdirectories || descriptor.Roles.Count == 0
        || descriptor.Roles.Any(static role => role is InputRole.UserInput or InputRole.Generated or InputRole.Unknown or InputRole.PotentialMembership);

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
