using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;

namespace RoslynMcpServer.Services.Testing.Snapshots;

/// <summary>
/// Two snapshots of the output assemblies of the baseline solution: the one before the edit, read once in
/// the background after publication, and the current one, read on request.
/// </summary>
/// <remarks>
/// The lifetime is the baseline's (owner decision M-10, docs/rapid-tia-2/mvp/README.md): the baseline
/// session calls <see cref="Start"/> for a new baseline and <see cref="Interrupt"/> wherever it closes a load
/// generation. A reload or <c>reset_workspace</c> of the same solution therefore keeps every assembly already
/// read; only the unfinished part of the first read becomes <see cref="AssemblySnapshotState.Unknown"/>.
/// Starting a new snapshot on a reset of the same solution would be wrong: after "edit, build, reset, load"
/// the rebuilt assemblies would pass for the state before the edit.
/// <para>
/// Nothing here keeps a <see cref="Solution"/>, a compilation or a file image: an entry holds the facts and
/// fingerprints parsed from its file. An entry that did not change is one object in both snapshots.
/// </para>
/// </remarks>
internal sealed class AssemblySnapshotSession
{
    internal AssemblySnapshotReader Reader { get; } = new();

    /// <summary>Runs before the first read of each assembly; the read is skipped if the snapshot was retired meanwhile.</summary>
    internal Func<string, CancellationToken, Task>? BeforeFirstReadForTests { get; set; }

    /// <summary>Runs at the start of each request for a pair of snapshots.</summary>
    internal Func<CancellationToken, Task>? BeforePairForTests { get; set; }

    /// <summary>
    /// The first read of the latest started snapshot, until it is interrupted; then the worker that is still
    /// stopping. Completed when no snapshot was started.
    /// </summary>
    internal Task FirstReadTask
    {
        get
        {
            lock (_gate)
            {
                return _firstReadTask;
            }
        }
    }

    /// <summary>How many requests have reached the gate that runs them one at a time, waiting or admitted.</summary>
    internal int RequestsAtGateForTests => Volatile.Read(ref _requestsAtGate);

    /// <summary>The output paths and assembly names of a solution, taken without keeping the solution.</summary>
    internal static AssemblySnapshotInventory Inventory(Solution solution)
    {
        var projects = solution.Projects.Where(project => !string.IsNullOrEmpty(project.OutputFilePath)).ToArray();
        return new AssemblySnapshotInventory(
            [.. projects.Select(project => project.OutputFilePath!).Distinct(IlPaths.Comparer)],
            projects.Select(project => project.AssemblyName).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The snapshot before the edit as far as it is read now, without waiting: an assembly whose first read
    /// has not finished has no entry yet. Null before the first baseline.
    /// </summary>
    internal AssemblySnapshot? PeekBefore()
    {
        lock (_gate)
        {
            return _snapshot is null ? null : BeforeUnderLock(_snapshot);
        }
    }

    /// <summary>Begins a new snapshot for a new baseline and retires the first read of the previous one.</summary>
    /// <returns>The cancellation of the retired read; the caller cancels it after leaving its locks.</returns>
    internal AssemblySnapshotRetirement Start(
        string workspacePath,
        DateTime publishedAtUtc,
        AssemblySnapshotInventory inventory,
        ILogger logger)
    {
        FirstRead work;
        AssemblySnapshotRetirement retired;
        lock (_gate)
        {
            retired = RetireFirstReadUnderLock(InterruptedReason);
            _snapshot = new Snapshot(Path.GetFullPath(workspacePath), publishedAtUtc, inventory);
            work = new FirstRead(_snapshot, BeforeFirstReadForTests);
            _firstRead = work;
            _firstReadTask = work.Completion.Task;
        }

        _ = Task.Run(() => ReadBeforeAsync(work, logger));
        return retired;
    }

    /// <summary>
    /// Stops the first read of the current snapshot without waiting for its worker: assemblies it has not
    /// read become <see cref="AssemblySnapshotState.Unknown"/>, and nothing it reads later is kept, even a
    /// read already in progress. Assemblies already read stay as they are.
    /// </summary>
    /// <returns>The cancellation of the retired read; the caller cancels it after leaving its locks.</returns>
    internal AssemblySnapshotRetirement Interrupt()
    {
        lock (_gate)
        {
            return RetireFirstReadUnderLock(InterruptedReason);
        }
    }

    private const string InterruptedReason =
        "The first read was interrupted by a reload or reset before it finished this assembly.";

    /// <summary>
    /// The pair of snapshots for the current projects of <paramref name="solution"/>. Test projects are the
    /// ones epoch 1 counts as such, so a test project of its answer gets its neighbors in the map. The
    /// solution is used during the call only. The request belongs to a baseline as the other overload says.
    /// </summary>
    internal async Task<AssemblySnapshotPair?> GetPairAsync(Solution solution, CancellationToken cancellationToken)
    {
        // Bound before the test projects are found: that parses every document and takes long enough for
        // another baseline to start, whose label would then go to this solution's projects (post-acceptance
        // review of task-03, round 1, PF-01).
        if (Bind() is not { } request)
        {
            return null;
        }

        var projects = await InputsAsync(solution, cancellationToken).ConfigureAwait(false);
        return await BuildPairAsync(request, projects, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the first read, rereads the output assemblies whose size or time changed since they were
    /// last read, and returns both snapshots with their graphs. Requests run one at a time, so concurrent
    /// requests read a changed file once. Null before the first baseline.
    /// </summary>
    /// <remarks>
    /// A request belongs to the baseline that is current when it starts, and the pair it returns is made of
    /// that baseline and <paramref name="projects"/> only. If a new baseline starts meanwhile, the request still
    /// answers for its own baseline: switching to the new one with the projects of the old solution would mix
    /// two solutions under one label (post-acceptance review of task-03, POST-F-01). Such a late answer leaves
    /// nothing in the session. A caller that combines the pair with other baseline data checks
    /// <see cref="AssemblySnapshotPair.BelongsTo"/>.
    /// </remarks>
    internal async Task<AssemblySnapshotPair?> GetPairAsync(IReadOnlyList<IlAssemblyInput> projects, CancellationToken cancellationToken)
    {
        if (Bind() is not { } request)
        {
            return null;
        }

        return await BuildPairAsync(request, projects, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The output assemblies of a solution, each marked when a test project of epoch 1 builds it. The answer
    /// depends on the solution only.
    /// </summary>
    /// <remarks>
    /// Finding the test projects parses every document, so a caller that asks for several pairs of one solution,
    /// such as a run that rereads the outputs after each project's build, takes this once. Taken before
    /// <see cref="GetPairAsync(IReadOnlyList{IlAssemblyInput}, CancellationToken)"/>, the inputs can belong to a
    /// solution older than the baseline the pair is bound to; the caller detects that with
    /// <see cref="AssemblySnapshotPair.BelongsTo"/>.
    /// </remarks>
    internal static async Task<IReadOnlyList<IlAssemblyInput>> InputsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var tests = await ProjectImpactMapper.FindTestProjectsAsync(solution, cancellationToken).ConfigureAwait(false);
        return
        [
            .. solution.Projects
                .Where(project => !string.IsNullOrEmpty(project.OutputFilePath))
                .GroupBy(project => project.OutputFilePath!, IlPaths.Comparer)
                .Select(group => new IlAssemblyInput(group.Key, group.Any(project => tests.Contains(project.Id)))),
        ];
    }

    /// <summary>The baseline a request starting now belongs to, with its first read; null before the first baseline.</summary>
    private PairRequest? Bind()
    {
        lock (_gate)
        {
            return _snapshot is null
                ? null
                : new PairRequest(_snapshot, _firstRead?.Completion.Task ?? Task.CompletedTask);
        }
    }

    private async Task<AssemblySnapshotPair> BuildPairAsync(
        PairRequest request,
        IReadOnlyList<IlAssemblyInput> projects,
        CancellationToken cancellationToken)
    {
        if (BeforePairForTests is { } beforePair)
        {
            await beforePair(cancellationToken).ConfigureAwait(false);
        }

        await request.FirstRead.WaitAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = request.Snapshot;
        string[] paths = [.. projects.Select(project => project.OutputFilePath).Distinct(IlPaths.Comparer)];
        AssemblySnapshot before;
        AssemblySnapshot current;
        Interlocked.Increment(ref _requestsAtGate);
        await _currentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AssemblySnapshotEntry> previous;
            lock (_gate)
            {
                previous = new Dictionary<string, AssemblySnapshotEntry>(snapshot.Current, IlPaths.Comparer);
            }

            var entries = new AssemblySnapshotEntry[paths.Length];
            Parallel.For(
                0,
                paths.Length,
                new ParallelOptions { CancellationToken = cancellationToken },
                index => entries[index] = Reader.ReadCurrent(
                    paths[index],
                    previous.GetValueOrDefault(paths[index]),
                    snapshot.Inventory.AssemblyNames));
            lock (_gate)
            {
                // A retired snapshot is detached from the session: caching into it would only keep memory.
                if (ReferenceEquals(_snapshot, snapshot))
                {
                    ReplaceCurrentUnderLock(snapshot, entries);
                }

                before = BeforeUnderLock(snapshot);
            }

            current = new AssemblySnapshot(entries);
        }
        finally
        {
            _currentGate.Release();
        }

        var neighbors = IlNeighborMapBuilder.Build(projects);
        return new AssemblySnapshotPair(
            snapshot.WorkspacePath,
            snapshot.PublishedAtUtc,
            before,
            current,
            neighbors,
            IlDependencyGraphBuilder.Build(GraphInputs(before), neighbors),
            IlDependencyGraphBuilder.Build(GraphInputs(current), neighbors));
    }

    /// <summary>
    /// Makes the cache of current entries the latest request's entries plus the reusable entries before the
    /// edit, so a path that left the solution is released.
    /// </summary>
    /// <remarks>
    /// Only adding entries kept every path any request had seen until the next baseline (post-acceptance
    /// review of task-03, POST-F-02). Falling back to the entry before the edit, rather than dropping the path,
    /// keeps an unchanged assembly stored once when a later request names it again. The snapshot before the
    /// edit is a map of its own and is not touched.
    /// </remarks>
    private static void ReplaceCurrentUnderLock(Snapshot snapshot, AssemblySnapshotEntry[] entries)
    {
        snapshot.Current.Clear();
        foreach (var entry in snapshot.Before.Values.Where(entry => entry.State != AssemblySnapshotState.Unknown))
        {
            snapshot.Current[entry.Path] = entry;
        }

        foreach (var entry in entries)
        {
            snapshot.Current[entry.Path] = entry;
        }
    }

    /// <summary>An unknown assembly gives no graph input: its content does not stand for any moment.</summary>
    private static IlAssemblyReadResult[] GraphInputs(AssemblySnapshot snapshot) =>
        [.. snapshot.Entries.Where(entry => entry.Result is not null).Select(entry => entry.Result!)];

    private async Task ReadBeforeAsync(FirstRead work, ILogger logger)
    {
        var outcome = "success";
        var elapsed = Stopwatch.StartNew();
        try
        {
            await Parallel.ForEachAsync(
                    work.Snapshot.Inventory.OutputPaths,
                    new ParallelOptions
                    {
                        CancellationToken = work.Token,
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, MaxParallelFirstReads),
                    },
                    async (path, token) => await ReadBeforeAsync(work, path, token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (work.Token.IsCancellationRequested)
        {
            outcome = "cancelled";
        }
        catch (Exception ex)
        {
            outcome = "failure";
            logger.LogWarning(ex, "assembly_snapshot first read failed");
        }
        finally
        {
            lock (_gate)
            {
                // A worker that failed leaves its unread assemblies unknown, as an interruption does. After a
                // success nothing is pending; the worker's own token needs no cancellation either way.
                if (ReferenceEquals(_firstRead, work))
                {
                    _ = RetireFirstReadUnderLock("The first read failed before it finished this assembly.");
                }
            }

            work.Cancellation.Dispose();
            work.Completion.TrySetResult();
        }

        logger.LogInformation(
            "assembly_snapshot first_read outcome={Outcome} assemblies={Assemblies} elapsed_ms={ElapsedMs}",
            outcome,
            work.Snapshot.Inventory.OutputPaths.Length,
            elapsed.ElapsedMilliseconds);
    }

    // The first read competes with the rest of a load (baseline capture, search prewarm); a few files at a
    // time keep it short without taking every core and holding many images at once.
    private const int MaxParallelFirstReads = 4;

    private async Task ReadBeforeAsync(FirstRead work, string path, CancellationToken cancellationToken)
    {
        if (work.BeforeFirstRead is not null)
        {
            await work.BeforeFirstRead(path, cancellationToken).ConfigureAwait(false);
        }

        if (!Admits(work, path))
        {
            return;
        }

        var entry = Reader.ReadFirst(path, work.Snapshot.PublishedAtUtc, work.Snapshot.Inventory.AssemblyNames);
        lock (_gate)
        {
            if (!AdmitsUnderLock(work, path))
            {
                return;
            }

            work.Snapshot.Before[path] = entry;
            work.Snapshot.Pending.Remove(path);

            // An unknown entry is not a read the current snapshot could reuse.
            if (entry.State != AssemblySnapshotState.Unknown)
            {
                work.Snapshot.Current[path] = entry;
            }
        }
    }

    private bool Admits(FirstRead work, string path)
    {
        lock (_gate)
        {
            return AdmitsUnderLock(work, path);
        }
    }

    // The worker is cancelled on retirement, but it may already be past every token check; admission under
    // the lock is what keeps a late result out of the snapshot.
    private bool AdmitsUnderLock(FirstRead work, string path) =>
        ReferenceEquals(_firstRead, work) && work.Snapshot.Pending.Contains(path);

    /// <summary>
    /// Freezes the unfinished part of the first read as <see cref="AssemblySnapshotState.Unknown"/> and detaches
    /// the worker, so that nothing it reads afterwards is admitted. The token is not cancelled here: see
    /// <see cref="AssemblySnapshotRetirement"/>.
    /// </summary>
    private AssemblySnapshotRetirement RetireFirstReadUnderLock(string reason)
    {
        if (_firstRead is null)
        {
            return default;
        }

        foreach (var path in _firstRead.Snapshot.Pending)
        {
            _firstRead.Snapshot.Before[path] = AssemblySnapshotEntry.Unknown(path, reason);
        }

        _firstRead.Snapshot.Pending.Clear();
        var retired = new AssemblySnapshotRetirement(_firstRead.Cancellation);
        _firstRead = null;
        return retired;
    }

    private static AssemblySnapshot BeforeUnderLock(Snapshot snapshot) =>
        new(snapshot.Inventory.OutputPaths
            .Where(snapshot.Before.ContainsKey)
            .Select(path => snapshot.Before[path]));

    /// <summary>The state of one baseline; replaced as a whole by the next baseline.</summary>
    private sealed class Snapshot
    {
        internal string WorkspacePath { get; }
        internal DateTime PublishedAtUtc { get; }
        internal AssemblySnapshotInventory Inventory { get; }

        /// <summary>Finished entries before the edit; an entry is never replaced once written.</summary>
        internal Dictionary<string, AssemblySnapshotEntry> Before { get; } = new(IlPaths.Comparer);

        internal HashSet<string> Pending { get; }

        /// <summary>The latest entry read for each path, shared with <see cref="Before"/> while the file is unchanged.</summary>
        internal Dictionary<string, AssemblySnapshotEntry> Current { get; } = new(IlPaths.Comparer);

        internal Snapshot(string workspacePath, DateTime publishedAtUtc, AssemblySnapshotInventory inventory)
        {
            WorkspacePath = workspacePath;
            PublishedAtUtc = publishedAtUtc;
            Inventory = inventory;
            Pending = new HashSet<string>(inventory.OutputPaths, IlPaths.Comparer);
        }
    }

    private sealed class FirstRead
    {
        internal Snapshot Snapshot { get; }
        internal Func<string, CancellationToken, Task>? BeforeFirstRead { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token { get; }
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal FirstRead(Snapshot snapshot, Func<string, CancellationToken, Task>? beforeFirstRead)
        {
            Snapshot = snapshot;
            BeforeFirstRead = beforeFirstRead;
            Token = Cancellation.Token;
        }
    }

    /// <summary>A pair request bound to its baseline: the snapshot and the first read it waits for.</summary>
    private readonly record struct PairRequest(Snapshot Snapshot, Task FirstRead);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _currentGate = new(1, 1);
    private Snapshot? _snapshot;
    private FirstRead? _firstRead;
    private Task _firstReadTask = Task.CompletedTask;
    private int _requestsAtGate;
}
