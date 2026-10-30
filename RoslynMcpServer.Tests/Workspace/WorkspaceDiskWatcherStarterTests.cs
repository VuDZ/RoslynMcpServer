using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

[Collection("DiskWatcherStartSeam")]
public sealed class WorkspaceDiskWatcherStarterTests
{
    [Fact]
    public async Task Point_and_nested_recursive_descriptors_inside_a_started_recursive_watcher_are_covered()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherCovered-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "project");
        var nestedProject = Path.Combine(projectDir, "tests");
        var objDir = Path.Combine(projectDir, "obj", "Release");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(nestedProject);
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(external);
        var objFile = Path.Combine(objDir, "App.AssemblyInfo.cs");

        var descriptors = new[]
        {
            new InputWatcherDescriptor(objDir, IncludeSubdirectories: false, new[] { objFile }, new[] { InputRole.Generated }, "point"),
            new InputWatcherDescriptor(nestedProject, IncludeSubdirectories: true, Array.Empty<string>(), new[] { InputRole.UserInput }, "nested"),
            new InputWatcherDescriptor(projectDir, IncludeSubdirectories: true, Array.Empty<string>(), new[] { InputRole.UserInput }, "membership"),
            new InputWatcherDescriptor(projectDir, IncludeSubdirectories: false, Array.Empty<string>(), new[] { InputRole.EvaluationInput }, "same"),
            new InputWatcherDescriptor(external, IncludeSubdirectories: false, Array.Empty<string>(), new[] { InputRole.EvaluationInput }, "external"),
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
            Assert.Equal(2, started.Watchers.Count);
            Assert.Contains(started.Subscriptions, subscription =>
                Same(subscription.Directory, projectDir) && subscription.IncludeSubdirectories);
            Assert.Contains(started.Subscriptions, subscription =>
                Same(subscription.Directory, external) && !subscription.IncludeSubdirectories);
            Assert.Empty(started.Failures);

            await File.WriteAllTextAsync(objFile, "// generated");
            Assert.True(await WaitUntil(() => Contains(seen, objFile)), "The recursive project watcher did not deliver the obj file.");
        }
        finally
        {
            DisposeWatchers(started.Watchers);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [Trait("Category", TestCategories.RequiresWindows)]
    [SupportedOSPlatform("windows")]
    public async Task Descriptor_across_a_junction_keeps_its_watcher_and_delivers_changes(bool recursive, bool nested)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherJunction-" + Guid.NewGuid().ToString("N")));
        var projectDir = Path.Combine(root, "project");
        var external = Path.Combine(root, "external");
        var junction = Path.Combine(projectDir, "linked");
        var childPath = nested ? Path.Combine("src", "child") : string.Empty;
        var targetDirectory = Path.Combine(external, childPath);
        var watchedDirectory = Path.Combine(junction, childPath);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(targetDirectory);
        WorkspaceDiskWatcherStartResult? started = null;
        try
        {
            await CreateJunctionAsync(junction, external);
            var session = new WorkspaceInputSession(Array.Empty<string>());
            var seen = new ConcurrentBag<string>();
            started = WorkspaceDiskWatcherStarter.Start(
                new[]
                {
                    new InputWatcherDescriptor(projectDir, IncludeSubdirectories: true, Array.Empty<string>(), new[] { InputRole.UserInput }, "parent"),
                    new InputWatcherDescriptor(watchedDirectory, recursive, Array.Empty<string>(), new[] { InputRole.UserInput }, "junction"),
                },
                session,
                onChanged: args => seen.Add(args.FullPath),
                onRenamed: _ => { },
                onError: _ => { },
                log: (_, _, _) => { });

            Assert.Equal(2, started.Watchers.Count);
            Assert.Contains(started.Subscriptions, subscription =>
                Same(subscription.Directory, watchedDirectory) && subscription.IncludeSubdirectories == recursive);
            Assert.Empty(started.Failures);

            await File.WriteAllTextAsync(Path.Combine(targetDirectory, "Changed.cs"), "class Changed {}");
            var expectedPath = Path.Combine(watchedDirectory, "Changed.cs");
            Assert.True(await WaitUntil(() => Contains(seen, expectedPath)), "The watcher across the junction did not deliver the file change.");
        }
        finally
        {
            DisposeWatchers(started?.Watchers ?? Array.Empty<FileSystemWatcher>());
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }

            TryDelete(root);
        }
    }

    [Fact]
    public void Point_descriptor_stays_when_the_covering_recursive_watcher_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpWatcherCoverFailed-" + Guid.NewGuid().ToString("N"));
        var objDir = Path.Combine(root, "obj");
        Directory.CreateDirectory(objDir);
        var session = new WorkspaceInputSession(Array.Empty<string>());
        WorkspaceDiskWatcherStarter.FailNextStartForTests = true;
        WorkspaceDiskWatcherStartResult? started = null;
        try
        {
            started = WorkspaceDiskWatcherStarter.Start(
                new[]
                {
                    new InputWatcherDescriptor(objDir, IncludeSubdirectories: false, Array.Empty<string>(), new[] { InputRole.Generated }, "point"),
                    new InputWatcherDescriptor(root, IncludeSubdirectories: true, Array.Empty<string>(), new[] { InputRole.UserInput }, "membership"),
                },
                session,
                onChanged: _ => { },
                onRenamed: _ => { },
                onError: _ => { },
                log: (_, _, _) => { });

            var subscription = Assert.Single(started.Subscriptions);
            Assert.True(Same(subscription.Directory, objDir));
            Assert.False(subscription.IncludeSubdirectories);
            var failure = Assert.Single(started.Failures);
            Assert.True(Same(failure.Directory, root));
            Assert.Equal("injected-watcher-start-failure", failure.Reason);
            Assert.Contains(InputCoverageReason.WatcherStartFailed, session.ObservationUnknownReasons);
        }
        finally
        {
            WorkspaceDiskWatcherStarter.FailNextStartForTests = false;
            DisposeWatchers(started?.Watchers ?? Array.Empty<FileSystemWatcher>());
            TryDelete(root);
        }
    }

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

    private static async Task CreateJunctionAsync(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", path, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        Assert.True((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0, "The fixture must create a real directory junction.");
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
