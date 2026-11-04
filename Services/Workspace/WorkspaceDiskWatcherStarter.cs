using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Starts one <see cref="FileSystemWatcher"/> per descriptor. Recursion is taken from the descriptor.
/// A non-recursive descriptor is never widened to an ancestor, and it is not promoted to recursive
/// when it shares a directory with a membership watcher. The file-name filter stays <c>*.*</c> so a
/// neighboring file in a point-watched directory is still delivered and classified by role.
/// Root lifetime subscriptions watch only directory names in the parent, never its descendant tree.
/// </summary>
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
        foreach (var descriptor in SubscriptionsToStart(descriptors))
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
                continue;
            }

            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(descriptor.Directory)
                {
                    IncludeSubdirectories = descriptor.IncludeSubdirectories,
                    NotifyFilter = descriptor.RootFilter is not null
                        ? NotifyFilters.DirectoryName
                        : NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                    Filter = descriptor.RootFilter ?? "*.*",
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

                if (descriptor.RootFilter is null)
                {
                    watcher.Changed += (_, args) => onChanged(args);
                    watcher.Created += (_, args) => onChanged(args);
                }
                var guardedPath = descriptor.RootFilter is null ? null : Path.Combine(descriptor.Directory, descriptor.RootFilter);
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
                    var directoryExists = Directory.Exists(descriptor.Directory);
                    var parent = Path.GetDirectoryName(descriptor.Directory);
                    // Windows reports AccessDenied when a watched root is deleted, after its child Deleted events.
                    // Journal mapped descendants through the same Deleted path without inventing an irreversible gap.
                    // Overflow still means unidentified loss even if the root vanished; existing-root errors remain gaps.
                    // See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-14-red.md (native root Error).
                    if (exception is not InternalBufferOverflowException && !directoryExists && !string.IsNullOrEmpty(parent))
                    {
                        onChanged(new FileSystemEventArgs(WatcherChangeTypes.Deleted, parent, Path.GetFileName(descriptor.Directory)));
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
                            $"Disk watcher error origin: {descriptor.Directory}; directoryExists={directoryExists}.");
                    }
                    catch (Exception)
                    {
                        // Diagnostics must not let a logging provider escape a native callback after the event was admitted.
                    }
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
                subscriptions.Add((descriptor.Directory, descriptor.IncludeSubdirectories));
                watcher = null;
                log(LogLevel.Information, null, "Disk watcher started on " + descriptor.Directory);
            }
            catch (Exception ex)
            {
                watcher?.Dispose();
                session.NoteCoverageUnknown(
                    InputCoverageReason.WatcherStartFailed,
                    CoverageScopeFor(descriptor.Directory));
                log(
                    LogLevel.Warning,
                    ex,
                    "Disk watcher failed to start for " + descriptor.Directory
                        + ". Symbol search stays on the load snapshot until reset_workspace.");
            }
        }

        return new WorkspaceDiskWatcherStartResult(watchers, subscriptions);
    }

    private static IEnumerable<(string Directory, bool IncludeSubdirectories, string? RootFilter)> SubscriptionsToStart(
        IReadOnlyList<InputWatcherDescriptor> descriptors)
    {
        var recursiveDirectories = descriptors.Where(static descriptor => descriptor.IncludeSubdirectories)
            .Select(static descriptor => descriptor.Directory).ToHashSet(InputPathCanon.Comparer);
        foreach (var descriptor in descriptors)
        {
            if (descriptor.IncludeSubdirectories || !recursiveDirectories.Contains(descriptor.Directory))
            {
                yield return (descriptor.Directory, descriptor.IncludeSubdirectories, null);
            }
        }

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
                    || candidate.IncludeSubdirectories && WorkspaceDiskPathFilter.IsPathUnderDirectory(
                        parent.FullName,
                        candidate.Directory,
                        InputPathCanon.Comparison)).ToArray();
                // A primary content subscription is not proof that its own root lifetime is protected.
                // Continue through excluded non-C# parents for the eligible descendant, without duplicate watches.
                if (covering.Any(NeedsRootLifetimeGuard))
                {
                    break;
                }
                if (covering.Length == 0)
                {
                    yield return (parent.FullName, false, Path.GetFileName(directory));
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
