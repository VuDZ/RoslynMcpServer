using System.Collections.Concurrent;
using System.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class BaselineDirectoryDisappearanceTests
{
    public BaselineDirectoryDisappearanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Native_move_outside_the_project_journals_glob_and_explicit_descendants(bool nestedSolution, bool explicitCompile)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source) = CreateLayout(fixture, nestedSolution, explicitCompile);
        await LoadKnown(fixture, solution, source);
        await MoveAndAssert(fixture, source, Path.Combine(fixture.Root, "Detached"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(bool parentSubscription)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true, linked: true);
        if (parentSubscription)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "L0.cs");
            WriteSource(sibling);
            AddCompile(Path.Combine(fixture.Root, "src", "App", "App.csproj"), sibling);
        }
        await LoadKnown(fixture, solution, source);
        Assert.Contains(fixture.Manager.DiskWatcherSubscriptions, subscription =>
            Same(subscription.Directory, Path.GetDirectoryName(source)!));
        await MoveAndAssert(fixture, source, Path.Combine(fixture.Root, "Detached"));
    }

    [Fact]
    public async Task Native_cross_parent_move_records_the_actual_platform_event_sequence()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true);
        var other = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "Other");
        Directory.CreateDirectory(other);
        await LoadKnown(fixture, solution, source);
        await MoveAndAssert(fixture, source, Path.Combine(other, "Sub"));
    }

    [Theory]
    [InlineData("artifacts")]
    [InlineData("obj")]
    public async Task Native_disappearance_keeps_mapped_inputs_under_ignored_segments(string segment)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true, segment: segment);
        await LoadKnown(fixture, solution, source);
        await MoveAndAssert(fixture, source, Path.Combine(fixture.Root, "Detached"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order(bool createFirst)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true);
        var second = Path.Combine(Path.GetDirectoryName(source)!, "B.cs");
        WriteSource(second);
        AddCompile(Path.Combine(fixture.Root, "src", "App", "App.csproj"), second);
        await LoadKnown(fixture, solution, source);
        fixture.Manager.SilenceDiskWatchersForTests();
        var original = Path.GetDirectoryName(source)!;
        var destination = Path.Combine(Path.GetDirectoryName(original)!, "Other", "Sub");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(original, destination);
        var paths = createFirst ? new[] { destination, original } : new[] { original, destination };
        foreach (var path in paths)
        {
            fixture.Manager.NotifyDiskWatcherChange(fixture.Manager.DiskWatcherSession!, new FileSystemEventArgs(
                Same(path, original) ? WatcherChangeTypes.Deleted : WatcherChangeTypes.Created,
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path)));
        }
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.Contains(source, view.TouchedPaths, InputPathCanon.Comparer);
        Assert.Contains(second, view.TouchedPaths, InputPathCanon.Comparer);
        await fixture.Manager.EnsureDiskChangesAppliedAsync();
        Assert.Contains(source, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_recursive_delete_journals_the_document_without_a_gap(bool linked)
    {
        var logger = new WorkspaceRecordingLogger();
        await using var fixture = new BaselineLifecycleFixture(logger);
        var (solution, source) = CreateLayout(fixture, nestedSolution: false, explicitCompile: true, linked: linked);
        await LoadKnown(fixture, solution, source);
        var session = fixture.Manager.DiskWatcherSession!;
        Directory.Delete(Path.GetDirectoryName(source)!, recursive: true);
        Assert.True(await WaitUntil(() => session.EventRevision(source) > 0), "The document deletion was not admitted.");
        if (OperatingSystem.IsWindows() && linked)
        {
            // Diagnostic settling only: a root Error may arrive after its child's Deleted event.
            // The no-gap assertion remains strict whether or not an Error was observed during this bounded window.
            await WaitUntil(() => logger.Exceptions.Count > 0, TimeSpan.FromSeconds(1));
        }
        var view = fixture.Manager.Baseline.GetSnapshot();
        var diagnostic = $"Linked={linked}; SourceEventRevision={session.EventRevision(source)}; RequiresFullScan={view.RequiresFullScan}."
            + Environment.NewLine + "Coverage reasons: " + string.Join(Environment.NewLine, session.ObservationUnknownReasons)
            + Environment.NewLine + "Touched: " + string.Join(Environment.NewLine, view.TouchedPaths)
            + Environment.NewLine + "Directory renames: " + string.Join(Environment.NewLine, session.DirectoryRenamePaths)
            + Environment.NewLine + "Workspace log: " + string.Join(Environment.NewLine, logger.Messages)
            + Environment.NewLine + "Exceptions: " + string.Join(Environment.NewLine, logger.Exceptions.Select(exception => exception.ToString()));
        _output.WriteLine(diagnostic);
        Assert.False(view.RequiresFullScan, diagnostic);
        Assert.Contains(source, view.TouchedPaths, InputPathCanon.Comparer);
    }

    [Theory]
    [InlineData("Empty", false)]
    [InlineData("Unmapped", true)]
    [InlineData("obj/Unmapped", true)]
    [InlineData("bin/Unmapped", true)]
    [InlineData("node_modules/Unmapped", true)]
    [InlineData(".git/Unmapped", true)]
    public async Task Native_unmapped_directory_disappearance_has_no_gap_and_ignored_paths_stay_out_of_journal(string relative, bool payload)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, _) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true);
        var directory = Path.GetFullPath(Path.Combine(fixture.Root, "src", "App", relative));
        Directory.CreateDirectory(directory);
        if (payload)
        {
            File.WriteAllText(Path.Combine(directory, "data.txt"), "data");
        }
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.AfterDiskWatcherChangeForTests = args =>
        {
            if (args.ChangeType == WatcherChangeTypes.Deleted && Same(args.FullPath, directory))
            {
                delivered.TrySetResult();
            }
        };
        Directory.Move(directory, Path.Combine(fixture.Root, "Detached"));
        await delivered.Task.WaitAsync(_timeout);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        if (WorkspaceDiskPathFilter.IsIgnoredPath(directory, Path.GetDirectoryName(solution)))
        {
            Assert.DoesNotContain(directory, view.TouchedPaths, InputPathCanon.Comparer);
        }
    }

    [Fact]
    public async Task Real_project_root_guard_count_excludes_standalone_non_csharp_ancestor_chains()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var descriptors = fixture.Manager.DiskWatcherSession!.InputMap!.Watchers;
        var actual = fixture.Manager.DiskWatcherSubscriptions;
        var extra = actual.Where(subscription => !descriptors.Any(descriptor => Same(descriptor.Directory, subscription.Directory)))
            .ToArray();
        var eligible = descriptors.Where(descriptor => descriptor.IncludeSubdirectories
            || descriptor.Roles.Count == 0
            || descriptor.Roles.Any(role => role is InputRole.UserInput or InputRole.Generated or InputRole.Unknown or InputRole.PotentialMembership))
            .ToArray();
        var unsupported = extra.Where(subscription => !eligible.Any(descriptor =>
            WorkspaceDiskPathFilter.IsPathUnderDirectory(descriptor.Directory, subscription.Directory, InputPathCanon.Comparison)))
            .ToArray();
        var diagnostic = $"Planned descriptors: {descriptors.Count}; actual watchers: {actual.Count}; extra roots: {extra.Length}."
            + Environment.NewLine + "Extra: " + string.Join(Environment.NewLine, extra.Select(subscription => subscription.Directory))
            + Environment.NewLine + "Unsupported: " + string.Join(Environment.NewLine, unsupported.Select(subscription => subscription.Directory));
        _output.WriteLine(diagnostic);
        Assert.True(unsupported.Length == 0, diagnostic);
    }

    [Fact]
    public async Task Linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, originalSource) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true, linked: true);
        var shared = Path.Combine(fixture.Root, "External", "Shared");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        Directory.Move(Path.GetDirectoryName(Path.GetDirectoryName(originalSource))!, shared);
        var source = Path.Combine(shared, "Sub", "A.cs");
        var project = Path.Combine(fixture.Root, "src", "App", "App.csproj");
        var text = File.ReadAllText(project).Replace(originalSource, source, StringComparison.Ordinal);
        File.WriteAllText(project, text.Replace(
            "<PropertyGroup>",
            $"<PropertyGroup><OutputPath>{System.Security.SecurityElement.Escape(shared)}</OutputPath><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>",
            StringComparison.Ordinal));
        await LoadKnown(fixture, solution, source);
        Assert.Contains(fixture.Manager.DiskWatcherSession!.InputMap!.Watchers, descriptor =>
            Same(descriptor.Directory, shared)
            && !descriptor.IncludeSubdirectories
            && descriptor.Roles.Count > 0
            && descriptor.Roles.All(role => role is InputRole.MetadataDependency or InputRole.Output));
        if (OperatingSystem.IsWindows())
        {
            // A Windows design-time load can deny moving this output directory. Check registration and Deleted admission
            // here; Linux exercises the native move. See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-14-red.md (boundary fixture limitation).
            Assert.Contains(fixture.Manager.DiskWatcherSubscriptions, subscription =>
                Same(subscription.Directory, Path.GetDirectoryName(shared)!) && !subscription.IncludeSubdirectories);
            fixture.Manager.SilenceDiskWatchersForTests();
            fixture.Manager.NotifyDiskWatcherChange(fixture.Manager.DiskWatcherSession!, new FileSystemEventArgs(
                WatcherChangeTypes.Deleted,
                Path.GetDirectoryName(shared)!,
                Path.GetFileName(shared)));
            Assert.Contains(source, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        }
        else
        {
            await MoveAndAssert(fixture, source, Path.Combine(fixture.Root, "Detached"), originalDirectory: shared);
        }
    }

    [Theory]
    [InlineData("Directory.Build.props", true, false)]
    [InlineData("Directory.Build.props", false, false)]
    [InlineData("Directory.Build.targets", true, false)]
    [InlineData("Directory.Build.targets", false, false)]
    [InlineData("Directory.Build.props", false, true)]
    public async Task Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(
        string candidateName,
        bool native,
        bool nestedCandidate)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, originalSource) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true);
        var projectDirectory = Path.Combine(fixture.Root, "src", "App");
        var directory = Path.Combine(projectDirectory, candidateName);
        Directory.CreateDirectory(directory);
        Directory.Move(Path.GetDirectoryName(originalSource)!, Path.Combine(directory, "Sub"));
        var source = Path.Combine(directory, "Sub", "A.cs");
        var project = Path.Combine(projectDirectory, "App.csproj");
        var projectText = File.ReadAllText(project);
        File.WriteAllText(project, projectText.Replace(originalSource, source, StringComparison.Ordinal));
        string? nestedSource = null;
        if (nestedCandidate)
        {
            var nestedDirectory = Path.Combine(directory, "Child", "Directory.Build.targets");
            Directory.CreateDirectory(nestedDirectory);
            nestedSource = Path.Combine(nestedDirectory, "Nested.cs");
            WriteSource(nestedSource);
            var childProject = Path.Combine(directory, "Child", "Child.csproj");
            File.WriteAllText(childProject, projectText.Replace(originalSource, nestedSource, StringComparison.Ordinal));
            var include = Path.GetRelativePath(Path.GetDirectoryName(solution)!, childProject).Replace('\\', '/');
            var solutionText = File.ReadAllText(solution);
            File.WriteAllText(solution, solutionText.Replace("</Solution>", $"<Project Path=\"{include}\" /></Solution>", StringComparison.Ordinal));
        }
        await LoadKnown(fixture, solution, source);
        var session = fixture.Manager.DiskWatcherSession!;
        var exact = session.InputMap!.Find(directory);
        Assert.NotNull(exact);
        Assert.False(exact.Present);
        Assert.True(Directory.Exists(directory));
        Assert.False(File.Exists(directory));
        if (nestedSource is not null)
        {
            var nestedExact = session.InputMap.Find(Path.GetDirectoryName(nestedSource)!);
            Assert.NotNull(nestedExact);
            Assert.False(nestedExact.Present);
        }
        var destination = Path.Combine(fixture.Root, "Detached");
        if (native)
        {
            await MoveAndAssert(fixture, source, destination, originalDirectory: directory);
        }
        else
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            var beforeSourceRevision = session.EventRevision(source);
            var beforeNestedRevision = nestedSource is null ? 0 : session.EventRevision(nestedSource);
            Directory.Move(directory, destination);
            fixture.Manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(
                WatcherChangeTypes.Deleted,
                projectDirectory,
                candidateName));
            var view = fixture.Manager.Baseline.GetSnapshot();
            Assert.False(view.RequiresFullScan);
            Assert.Contains(source, view.TouchedPaths, InputPathCanon.Comparer);
            Assert.Equal(beforeSourceRevision + 1, session.EventRevision(source));
            if (nestedSource is not null)
            {
                Assert.Contains(nestedSource, view.TouchedPaths, InputPathCanon.Comparer);
                Assert.Equal(beforeNestedRevision + 1, session.EventRevision(nestedSource));
            }
        }
    }

    [Theory]
    [InlineData("Scratch_wpftmp.csproj", "A.cs", true)]
    [InlineData("Scratch_wpftmp.csproj", "A.cs", false)]
    [InlineData("Scratch", "Item_wpftmp.csproj", true)]
    [InlineData("Scratch", "Item_wpftmp.csproj", false)]
    [InlineData("Scratch_wpftmp.csproj", "Item_wpftmp.csproj", false)]
    public async Task Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(
        string directoryName,
        string sourceName,
        bool native)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source, directory) = CreateNamedCompileLayout(fixture, directoryName, sourceName);
        await LoadKnown(fixture, solution, source);
        var session = fixture.Manager.DiskWatcherSession!;
        Assert.Contains(session.InputMap!.Find(source)!.Occurrences, occurrence => occurrence.DocumentId is not null);
        var destination = Path.Combine(fixture.Root, "Detached");
        if (native)
        {
            await MoveAndAssert(fixture, source, destination, originalDirectory: directory);
        }
        else
        {
            fixture.Manager.SilenceDiskWatchersForTests();
            Directory.Move(directory, destination);
            fixture.Manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(
                WatcherChangeTypes.Deleted,
                Path.GetDirectoryName(directory)!,
                Path.GetFileName(directory)));
            var view = fixture.Manager.Baseline.GetSnapshot();
            Assert.False(view.RequiresFullScan);
            Assert.Contains(source, view.TouchedPaths, InputPathCanon.Comparer);
        }
    }

    [Fact]
    public async Task Deleted_wpf_named_compile_file_is_journaled_without_expanding_wpf_semantic_behavior()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var (solution, source, _) = CreateNamedCompileLayout(fixture, "Scratch", "Item_wpftmp.csproj");
        await LoadKnown(fixture, solution, source);
        fixture.Manager.SilenceDiskWatchersForTests();
        var session = fixture.Manager.DiskWatcherSession!;
        var revision = session.EventRevision(source);
        File.Delete(source);
        fixture.Manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(
            WatcherChangeTypes.Deleted,
            Path.GetDirectoryName(source)!,
            Path.GetFileName(source)));
        Assert.Contains(source, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.Equal(revision, session.EventRevision(source));
        Assert.Empty(fixture.Manager.GetPendingDirtySourcePaths());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_unmapped_wpf_temporary_file_keeps_baseline_and_semantic_suppression(bool mapless)
    {
        await using var fixture = new BaselineLifecycleFixture();
        var temporary = Path.Combine(fixture.Root, "Scratch_wpftmp.csproj");
        File.WriteAllText(temporary, "<Project />");
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        fixture.Manager.SilenceDiskWatchersForTests();
        var session = mapless ? fixture.Manager.StartDiskWatcherSessionForTests(Array.Empty<string>()) : fixture.Manager.DiskWatcherSession!;
        if (!mapless)
        {
            Assert.Null(session.InputMap!.Find(temporary));
        }
        File.Delete(temporary);
        fixture.Manager.NotifyDiskWatcherChange(session, new FileSystemEventArgs(
            WatcherChangeTypes.Deleted,
            fixture.Root,
            Path.GetFileName(temporary)));
        Assert.DoesNotContain(temporary, fixture.Manager.Baseline.GetSnapshot().TouchedPaths, InputPathCanon.Comparer);
        Assert.False(fixture.Manager.ProjectGraphStale);
        Assert.False(fixture.Manager.RefreshAllDocumentsPending);
        Assert.Equal(0, session.EventRevision(temporary));
    }

    private static (string Solution, string Source, string Directory) CreateNamedCompileLayout(
        BaselineLifecycleFixture fixture,
        string directoryName,
        string sourceName)
    {
        var (solution, originalSource) = CreateLayout(fixture, nestedSolution: true, explicitCompile: true);
        var projectDirectory = Path.Combine(fixture.Root, "src", "App");
        var directory = Path.Combine(projectDirectory, directoryName);
        Directory.CreateDirectory(directory);
        Directory.Move(Path.GetDirectoryName(originalSource)!, Path.Combine(directory, "Sub"));
        var source = Path.Combine(directory, "Sub", sourceName);
        if (sourceName != "A.cs")
        {
            File.Move(Path.Combine(directory, "Sub", "A.cs"), source);
        }
        var project = Path.Combine(projectDirectory, "App.csproj");
        var text = File.ReadAllText(project);
        File.WriteAllText(project, text.Replace(originalSource, source, StringComparison.Ordinal));
        return (solution, source, directory);
    }

    private async Task MoveAndAssert(
        BaselineLifecycleFixture fixture,
        string source,
        string destination,
        string? originalDirectory = null)
    {
        var original = originalDirectory ?? Path.GetDirectoryName(source)!;
        var trace = new ConcurrentQueue<string>();
        using var observer = new FileSystemWatcher(fixture.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
        };
        observer.Deleted += (_, args) => trace.Enqueue("Deleted " + args.FullPath);
        observer.Created += (_, args) => trace.Enqueue("Created " + args.FullPath);
        observer.Renamed += (_, args) => trace.Enqueue("Renamed " + args.OldFullPath + " -> " + args.FullPath);
        observer.EnableRaisingEvents = true;
        Directory.Move(original, destination);
        Assert.True(await WaitUntil(() => trace.Any(line => line.Contains(original, StringComparison.Ordinal))),
            "Independent native observer did not receive the directory operation.");
        var covered = await WaitUntil(() =>
        {
            var current = fixture.Manager.Baseline.GetSnapshot();
            return current.RequiresFullScan || current.TouchedPaths.Contains(source, InputPathCanon.Comparer);
        });
        observer.EnableRaisingEvents = false;
        var events = string.Join(Environment.NewLine, trace);
        _output.WriteLine(events);
        Assert.True(covered, "Mapped descendant was omitted without a baseline gap. Native sequence:" + Environment.NewLine + events);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.Equal(BaselineDocumentStatus.Known, Assert.Single(view.Documents,
            document => Same(document.Key.DocumentPath, source)).Status);
    }

    private static (string Solution, string Source) CreateLayout(
        BaselineLifecycleFixture fixture,
        bool nestedSolution,
        bool explicitCompile,
        bool linked = false,
        string? segment = null)
    {
        var repository = segment == "artifacts" ? Path.Combine(fixture.Root, segment) : fixture.Root;
        var projectDirectory = Path.Combine(repository, "src", "App");
        Directory.CreateDirectory(projectDirectory);
        var sourceDirectory = linked ? Path.Combine(fixture.Root, "Shared", "Sub")
            : Path.Combine(projectDirectory, segment == "obj" ? "obj/Sub" : "Sub");
        Directory.CreateDirectory(sourceDirectory);
        var source = Path.GetFullPath(Path.Combine(sourceDirectory, "A.cs"));
        WriteSource(source);
        var project = Path.Combine(projectDirectory, "App.csproj");
        var compile = explicitCompile || linked || segment == "obj"
            ? $"<Compile Include=\"{System.Security.SecurityElement.Escape(source)}\" />"
            : "<Compile Include=\"**/*.cs\" Exclude=\"obj/**;bin/**\" />";
        File.WriteAllText(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                <ImplicitUsings>disable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>{{compile}}</ItemGroup>
            </Project>
            """);
        var solutionDirectory = nestedSolution ? Path.Combine(repository, "build") : repository;
        Directory.CreateDirectory(solutionDirectory);
        var solution = Path.Combine(solutionDirectory, "All.slnx");
        var relative = Path.GetRelativePath(solutionDirectory, project).Replace('\\', '/');
        File.WriteAllText(solution, $"<Solution><Project Path=\"{relative}\" /></Solution>");
        return (solution, source);
    }

    private static async Task LoadKnown(BaselineLifecycleFixture fixture, string solution, string source)
    {
        await fixture.Manager.LoadAsync(solution);
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(_timeout);
        var view = fixture.Manager.Baseline.GetSnapshot();
        Assert.False(view.RequiresFullScan);
        Assert.Equal(BaselineDocumentStatus.Known, Assert.Single(view.Documents,
            document => Same(document.Key.DocumentPath, source)).Status);
        Assert.DoesNotContain(source, view.TouchedPaths, InputPathCanon.Comparer);
    }

    private static void AddCompile(string project, string source)
    {
        var text = File.ReadAllText(project);
        File.WriteAllText(project, text.Replace(
            "<ItemGroup>",
            $"<ItemGroup><Compile Include=\"{System.Security.SecurityElement.Escape(source)}\" />",
            StringComparison.Ordinal));
    }

    private static void WriteSource(string path)
    {
        File.WriteAllText(path, "class Mapped {}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
    }

    private static bool Same(string left, string right) =>
        InputPathCanon.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < (timeout ?? _timeout))
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(20);
        }
        return condition();
    }

    private readonly ITestOutputHelper _output;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(8);
}
