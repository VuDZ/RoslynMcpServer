using System.Collections.Concurrent;
using System.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WpfTemporaryProjectWatchingTests
{
    [WpfFact]
    public async Task Real_WPF_build_preserves_the_cached_graph_after_flush_and_repeated_loads()
    {
        MsBuildBootstrapper.Register();
        using var fixture = new WpfTemporaryProjectFixture();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        var evidence = new StringBuilder();
        var events = new ConcurrentQueue<string>();
        var temporaryDeleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterDiskWatcherChangeForTests = args =>
        {
            if (args.ChangeType == WatcherChangeTypes.Deleted
                && args.FullPath.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
            {
                temporaryDeleted.TrySetResult();
            }
        };
        using var observer = new FileSystemWatcher(fixture.Root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
        };
        observer.Created += (_, args) => events.Enqueue($"Created {args.FullPath}");
        observer.Changed += (_, args) => events.Enqueue($"Changed {args.FullPath}");
        observer.Deleted += (_, args) => events.Enqueue($"Deleted {args.FullPath}");
        observer.Error += (_, args) => events.Enqueue($"Error {args.GetException()}");
        try
        {
            evidence.AppendLine("SDK " + (await fixture.RunDotNetAsync("--version")).Trim());
            evidence.AppendLine("Command: dotnet build " + fixture.SolutionPath + " --no-incremental --nologo");
            evidence.AppendLine("Command CWD: " + fixture.Root);
            await fixture.BuildAsync();
            var projectBefore = await File.ReadAllTextAsync(fixture.ProjectPath);
            var solutionBefore = await File.ReadAllTextAsync(fixture.SolutionPath);
            await manager.LoadAsync(fixture.SolutionPath);
            var session = manager.DiskWatcherSession!;
            RecordState(evidence, "before build", manager);
            observer.EnableRaisingEvents = true;
            evidence.AppendLine(await fixture.BuildAsync());
            await temporaryDeleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            RecordState(evidence, "after delivery", manager);
            Assert.False(manager.ProjectGraphStale);
            Assert.False(manager.RefreshAllDocumentsPending);
            await manager.EnsureDiskChangesAppliedAsync();
            RecordState(evidence, "after semantic flush", manager);
            Assert.False(manager.ProjectGraphStale);
            Assert.Empty(manager.GetPendingDirtySourcePaths());
            await manager.LoadAsync(fixture.SolutionPath);
            RecordState(evidence, "load 1", manager);
            Assert.True(manager.LastLoadWasCacheHit);
            Assert.False(manager.ProjectGraphStale);
            await manager.LoadAsync(fixture.SolutionPath);
            RecordState(evidence, "load 2", manager);
            Assert.True(manager.LastLoadWasCacheHit);
            Assert.False(manager.ProjectGraphStale);
            Assert.Same(session, manager.DiskWatcherSession);
            Assert.Empty(manager.GetPendingDirtySourcePaths());
            evidence.AppendLine("EVENTS\n" + string.Join('\n', events));
            evidence.AppendLine("INFORMATION LOG\n" + string.Join('\n', logger.Messages));
            foreach (var changeType in new[] { "Created", "Changed", "Deleted" })
            {
                Assert.Contains(events, entry => entry.StartsWith(changeType + " ", StringComparison.Ordinal)
                    && entry.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
            }
            Assert.DoesNotContain(events, entry => entry.StartsWith("Error ", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, entry => entry.StartsWith("Project graph file changed on disk:")
                && entry.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(projectBefore, await File.ReadAllTextAsync(fixture.ProjectPath));
            Assert.Equal(solutionBefore, await File.ReadAllTextAsync(fixture.SolutionPath));
            manager.NotifyDiskWatcherError(new InternalBufferOverflowException("Integration overflow probe."));
            RecordState(evidence, "overflow callback", manager);
            Assert.True(manager.RefreshAllDocumentsPending);
            Assert.False(manager.ProjectGraphStale);
            await manager.LoadAsync(fixture.SolutionPath);
            RecordState(evidence, "overflow load 1 flush", manager);
            Assert.True(manager.LastLoadWasCacheHit);
            Assert.True(manager.ProjectGraphStale);
            Assert.True(manager.ProjectGraphStaleFromComposition);
            Assert.False(manager.ProjectGraphStaleFromGraphFile);
            await manager.LoadAsync(fixture.SolutionPath);
            RecordState(evidence, "overflow load 2", manager);
            Assert.True(manager.LastLoadReopenedGraph);
            Assert.False(manager.ProjectGraphStale);
            Assert.NotEqual(session.Generation, manager.DiskWatcherSession!.Generation);
            manager.NotifyDiskWatcherError(session, new InternalBufferOverflowException("Late overflow probe."));
            manager.NotifyDiskWatcherChange(session,
                new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(fixture.ProjectPath)!,
                    Path.GetFileName(fixture.ProjectPath)));
            Assert.False(manager.ProjectGraphStale);
            Assert.False(manager.RefreshAllDocumentsPending);
            evidence.AppendLine("OVERFLOW LOG\n" + string.Join('\n', logger.Messages
                .Where(entry => entry.Contains("composition", StringComparison.Ordinal) || entry.Contains("watcher error", StringComparison.Ordinal))));
        }
        finally
        {
            evidence.AppendLine("FINAL LOG\n" + string.Join('\n', logger.Messages));
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "RoslynWpfWatching-evidence.txt"), evidence.ToString());
            await manager.ClearWorkspaceAsync();
        }
    }

    [WpfFact]
    public async Task Real_project_edit_delivered_during_WPF_build_still_invalidates_the_graph()
    {
        MsBuildBootstrapper.Register();
        using var fixture = new WpfTemporaryProjectFixture();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        using var observer = new FileSystemWatcher(fixture.Root) { IncludeSubdirectories = true };
        var edited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Created += (_, args) =>
        {
            if (!args.FullPath.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                // The temporary project is written before its internal compilation. Inject a real
                // graph edit while that build is running to catch whole-build suppression regressions.
                File.AppendAllText(fixture.ProjectPath, "\n<!-- External edit during WPF temporary compilation. -->\n");
                edited.TrySetResult();
            }
            catch (Exception ex)
            {
                edited.TrySetException(ex);
            }
        };
        try
        {
            await fixture.BuildAsync();
            await manager.LoadAsync(fixture.SolutionPath);
            observer.EnableRaisingEvents = true;
            await fixture.BuildAsync();
            await edited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!manager.ProjectGraphStaleFromGraphFile && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            Assert.True(manager.ProjectGraphStaleFromGraphFile);
            Assert.False(manager.ProjectGraphStaleFromComposition);
            Assert.Contains(logger.Messages, message => message == "Project graph file changed on disk: " + fixture.ProjectPath);
            Assert.DoesNotContain(logger.Messages, message => message.StartsWith("Project graph file changed on disk:")
                && message.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    private static void RecordState(StringBuilder evidence, string stage, SolutionManager manager)
    {
        evidence.AppendLine($"{stage}: stale={manager.ProjectGraphStale} graphFile={manager.ProjectGraphStaleFromGraphFile} "
            + $"composition={manager.ProjectGraphStaleFromComposition} reason={manager.GetProjectGraphStaleHint()} "
            + $"refreshAll={manager.RefreshAllDocumentsPending} dirty={manager.GetPendingDirtySourcePaths().Count} "
            + $"cacheHit={manager.LastLoadWasCacheHit} reopen={manager.LastLoadReopenedGraph}");
    }
}
