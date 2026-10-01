using System.Collections.Concurrent;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceDiskWatcherStarterTests
{
    [Fact]
    public async Task Non_recursive_watcher_sees_the_file_and_not_a_foreign_nested_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherStart-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "project");
        var ancestor = Path.Combine(root, "ancestor");
        var nested = Path.Combine(ancestor, "nested");
        Directory.CreateDirectory(Path.Combine(projectDir, "Nested"));
        Directory.CreateDirectory(nested);
        var seenPath = Path.Combine(projectDir, "Nested", "Seen.cs");
        var propsPath = Path.Combine(ancestor, "Directory.Build.props");
        var foreignPath = Path.Combine(nested, "Foreign.cs");
        await File.WriteAllTextAsync(seenPath, "class Seen {}");
        await File.WriteAllTextAsync(propsPath, "<Project />");

        var descriptors = new[]
        {
            new InputWatcherDescriptor(projectDir, IncludeSubdirectories: true, Array.Empty<string>(), new[] { InputRole.UserInput }, "membership"),
            new InputWatcherDescriptor(ancestor, IncludeSubdirectories: false, new[] { propsPath }, new[] { InputRole.EvaluationInput }, "point"),
        };
        var session = new WorkspaceInputSession(Array.Empty<string>());
        var seen = new ConcurrentBag<string>();
        var started = WorkspaceDiskWatcherStarter.Start(
            descriptors,
            session,
            onChanged: args => seen.Add(args.FullPath),
            onRenamed: _ => { },
            onError: _ => { },
            log: (_, _, _) => { });
        try
        {
            Assert.Contains(started.Subscriptions, subscription =>
                Same(subscription.Directory, projectDir) && subscription.IncludeSubdirectories);
            Assert.Contains(started.Subscriptions, subscription =>
                Same(subscription.Directory, ancestor) && !subscription.IncludeSubdirectories);
            Assert.DoesNotContain(started.Subscriptions, subscription =>
                subscription.IncludeSubdirectories && Same(subscription.Directory, ancestor));

            await File.AppendAllTextAsync(seenPath, " ");
            await File.AppendAllTextAsync(propsPath, " ");
            Assert.True(await WaitUntil(() => Contains(seen, seenPath) && Contains(seen, propsPath)), "The membership and point watchers did not deliver.");

            await File.WriteAllTextAsync(foreignPath, "class Foreign {}");
            await Task.Delay(1500);
            Assert.DoesNotContain(seen, path => Same(path, foreignPath));
        }
        finally
        {
            DisposeWatchers(started.Watchers);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Absent_props_file_is_seen_by_the_non_recursive_watcher()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherAbsent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var propsPath = Path.Combine(root, "Directory.Build.targets");
        Assert.False(File.Exists(propsPath));
        var session = new WorkspaceInputSession(Array.Empty<string>());
        var seen = new ConcurrentBag<string>();
        var started = WorkspaceDiskWatcherStarter.Start(
            new[]
            {
                new InputWatcherDescriptor(root, IncludeSubdirectories: false, new[] { propsPath }, new[] { InputRole.EvaluationInput }, "absent"),
            },
            session,
            onChanged: args => seen.Add(args.FullPath),
            onRenamed: _ => { },
            onError: _ => { },
            log: (_, _, _) => { });
        try
        {
            await File.WriteAllTextAsync(propsPath, "<Project />");
            Assert.True(await WaitUntil(() => Contains(seen, propsPath)), "Appearance of the absent props file was not delivered.");
            Assert.All(started.Subscriptions, subscription => Assert.False(subscription.IncludeSubdirectories));
        }
        finally
        {
            DisposeWatchers(started.Watchers);
            TryDelete(root);
        }
    }

    [Fact]
    public void Missing_directory_does_not_widen_to_an_ancestor_watcher()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherMissing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var missing = Path.Combine(root, "missing-child");
        var session = new WorkspaceInputSession(Array.Empty<string>());
        var started = WorkspaceDiskWatcherStarter.Start(
            new[]
            {
                new InputWatcherDescriptor(missing, IncludeSubdirectories: false, Array.Empty<string>(), new[] { InputRole.EvaluationInput }, "missing"),
            },
            session,
            onChanged: _ => { },
            onRenamed: _ => { },
            onError: _ => { },
            log: (_, _, _) => { });
        try
        {
            Assert.Empty(started.Watchers);
            Assert.Empty(started.Subscriptions);
            Assert.Contains(InputCoverageReason.WatcherDirectoryMissing, session.ObservationUnknownReasons);
            Assert.Equal(InputCoverage.Unknown, session.ObservationCoverage);
            Assert.DoesNotContain(started.Watchers, watcher => Same(watcher.Path, root));
        }
        finally
        {
            DisposeWatchers(started.Watchers);
            TryDelete(root);
        }
    }

    private static bool Contains(ConcurrentBag<string> seen, string path)
    {
        return seen.Any(item => Same(item, path));
    }

    private static async Task<bool> WaitUntil(Func<bool> ready)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
        {
            if (ready())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return ready();
    }

    private static bool Same(string left, string right)
    {
        return InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    }

    private static void DisposeWatchers(IReadOnlyList<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
