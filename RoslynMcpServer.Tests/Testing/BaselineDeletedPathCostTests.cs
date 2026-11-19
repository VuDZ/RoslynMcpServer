using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineDeletedPathCostTests
{
    public BaselineDeletedPathCostTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(50, "node_modules")]
    [InlineData(200, "node_modules")]
    [InlineData(50, "obj")]
    [InlineData(200, "obj")]
    [InlineData(50, "bin")]
    [InlineData(200, "artifacts")]
    [InlineData(200, "ordinary")]
    public void Empty_deleted_path_does_not_enumerate_paths_or_occurrences(int sourceCount, string directoryName)
    {
        var manager = SolutionManagerTestFactory.Create();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDeletedProbe-" + Guid.NewGuid().ToString("N"));
        var session = manager.StartDiskWatcherSessionForTests([Path.Combine(root, "App.csproj")]);
        var projectId = ProjectId.CreateNewId();
        var occurrences = new List<CountedList<InputOccurrence>>();
        var entries = new List<InputPathEntry>();
        for (var index = 0; index < sourceCount; index++)
        {
            var counted = new CountedList<InputOccurrence>(
                [new InputOccurrence(projectId, DocumentId.CreateNewId(projectId), InputRole.UserInput, null, Present: true)]);
            occurrences.Add(counted);
            entries.Add(new InputPathEntry(Path.Combine(root, "Sources", "C" + index + ".cs"), counted));
        }
        var paths = new CountedList<InputPathEntry>(entries);
        var map = new WorkspaceInputMap(
            session.Generation,
            false,
            InputCoverage.Unknown,
            InputCoverage.Unknown,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<LoadedProjectInstance>(),
            paths,
            [new MembershipRegion(root, true, projectId, "Loaded project directory.", false)],
            Array.Empty<InputWatcherDescriptor>(),
            Array.Empty<InputCategoryEvidence>(),
            [root]);
        Assert.True(session.TryPublishInputMap(map));
        // Construction may index the inputs; callback cost is measured only after publication.
        paths.Reset();
        foreach (var counted in occurrences)
        {
            counted.Reset();
        }
        var path = Path.Combine(root, directoryName, "empty", "deleted.txt");
        manager.NotifyDiskWatcherChange(session,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        var occurrenceReads = occurrences.Sum(counted => counted.Reads);
        _output.WriteLine($"Sources={sourceCount}; directory={directoryName}; pathReads={paths.Reads}; occurrenceReads={occurrenceReads}.");
        Assert.Equal(0, paths.Reads);
        Assert.Equal(0, occurrenceReads);
    }

    [Theory]
    [InlineData(50, 20000, "node_modules")]
    [InlineData(200, 20000, "node_modules")]
    [InlineData(2000, 5000, "obj")]
    public async Task Native_mass_delete_measures_backlog_and_keeps_node_modules_gap_free(
        int sourceCount,
        int payloadCount,
        string directoryName)
    {
        MsBuildBootstrapper.Register();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDeleteCost-" + Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "Sources");
        var payload = Path.Combine(root, directoryName, directoryName == "obj" ? "out" : "x");
        Directory.CreateDirectory(sourceDirectory);
        var sourceTime = DateTime.UtcNow.AddDays(-1);
        for (var index = 0; index < sourceCount; index++)
        {
            var source = Path.Combine(sourceDirectory, "C" + index + ".cs");
            File.WriteAllText(source, $"class C{index} {{ }}");
            File.SetLastWriteTimeUtc(source, sourceTime);
        }
        for (var directory = 0; directory < 40; directory++)
        {
            Directory.CreateDirectory(Path.Combine(payload, directory.ToString()));
        }
        for (var index = 0; index < payloadCount; index++)
        {
            File.WriteAllText(Path.Combine(payload, (index % 40).ToString(), "f" + index + ".txt"), "payload");
        }
        var project = Path.Combine(root, "App.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                <ImplicitUsings>disable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup><Compile Include="Sources/**/*.cs" /></ItemGroup>
            </Project>
            """);
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            await manager.LoadAsync(project);
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(120));
            var initial = manager.Baseline.GetSnapshot();
            Assert.False(initial.RequiresFullScan);
            Assert.Equal(sourceCount, initial.Documents.Length);
            Assert.All(initial.Documents, document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
            Assert.False(manager.ProjectGraphStale);
            Assert.DoesNotContain(initial.TouchedPaths,
                path => WorkspaceDiskPathFilter.IsPathUnderDirectory(path, payload, InputPathCanon.Comparison));
            var session = manager.DiskWatcherSession!;
            var deleted = 0;
            long lastDeleted = 0;
            var fence = Path.Combine(root, "WatcherFence.txt");
            var fenceSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.AfterDiskWatcherChangeForTests = args =>
            {
                if (args.ChangeType == WatcherChangeTypes.Deleted
                    && WorkspaceDiskPathFilter.IsPathUnderDirectory(args.FullPath, payload, InputPathCanon.Comparison))
                {
                    Interlocked.Increment(ref deleted);
                    Interlocked.Exchange(ref lastDeleted, Stopwatch.GetTimestamp());
                }
                if (InputPathCanon.Comparer.Equals(args.FullPath, fence))
                {
                    fenceSeen.TrySetResult();
                }
            };
            var started = Stopwatch.GetTimestamp();
            Directory.Delete(payload, recursive: true);
            var returned = Stopwatch.GetTimestamp();
            var deadline = Stopwatch.StartNew();
            var attempts = 0;
            while (!fenceSeen.Task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(90))
            {
                // A fence after the delete acknowledges the recursive watcher's queue, even after an overflow dropped an earlier fence.
                File.WriteAllText(fence, (++attempts).ToString());
                await Task.WhenAny(fenceSeen.Task, Task.Delay(100));
            }
            var view = manager.Baseline.GetSnapshot();
            var last = Interlocked.Read(ref lastDeleted);
            var ignoredJournal = view.TouchedPaths.Count(path =>
                WorkspaceDiskPathFilter.IsPathUnderDirectory(path, payload, InputPathCanon.Comparison));
            var report = JsonSerializer.Serialize(new
            {
                platform = OperatingSystem.IsWindows() ? "Windows" : "Linux",
                sourceCount,
                payloadCount,
                directoryName,
                mapPaths = session.InputMap!.Paths.Count,
                deleteMs = Stopwatch.GetElapsedTime(started, returned).TotalMilliseconds,
                callbackTailMs = last <= returned ? 0 : Stopwatch.GetElapsedTime(returned, last).TotalMilliseconds,
                handledDeleted = Volatile.Read(ref deleted),
                expectedDeleted = payloadCount + 41,
                fenceSeen = fenceSeen.Task.IsCompleted,
                fenceAttempts = attempts,
                requiresFullScan = view.RequiresFullScan,
                ignoredJournal,
                reasons = session.ObservationUnknownReasons,
                exceptions = logger.Exceptions.Select(exception => exception.GetType().Name + ": " + exception.Message).Distinct().ToArray(),
            });
            _output.WriteLine(report);
            Assert.True(fenceSeen.Task.IsCompleted, report);
            if (directoryName == "node_modules")
            {
                Assert.False(view.RequiresFullScan, report);
                Assert.Equal(0, ignoredJournal);
            }
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(120));
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("bin")]
    [InlineData("artifacts")]
    public async Task Empty_ignored_deleted_path_keeps_baseline_suppression(string directoryName)
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        fixture.Manager.SilenceDiskWatchersForTests();
        var path = Path.Combine(fixture.Root, directoryName, "empty", "deleted.tmp");
        fixture.Manager.NotifyDiskWatcherChange(fixture.Manager.DiskWatcherSession!,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.DoesNotContain(path, view.TouchedPaths, InputPathCanon.Comparer);
    }

    private sealed class CountedList<T> : IReadOnlyList<T>
    {
        public int Reads { get; private set; }

        public int Count
        {
            get
            {
                Reads++;
                return _items.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                Reads++;
                return _items[index];
            }
        }

        public CountedList(IReadOnlyList<T> items) => _items = items;

        public void Reset() => Reads = 0;

        public IEnumerator<T> GetEnumerator()
        {
            Reads++;
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private readonly IReadOnlyList<T> _items;
    }

    private readonly ITestOutputHelper _output;
}
