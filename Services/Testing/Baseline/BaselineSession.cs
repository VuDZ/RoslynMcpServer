using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing.Baseline;

internal sealed class BaselineSession
{
    internal Task CaptureTask
    {
        get
        {
            lock (_gate)
            {
                return _captureTask;
            }
        }
    }
    internal Func<BaselineDocumentKey, CancellationToken, Task>? BeforeReadForTests { get; set; }
    internal Func<BaselineDocumentKey, CancellationToken, Task>? BeforeCommitForTests { get; set; }

    internal BaselineSessionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var touched = _touched.Union(_pendingWrites).ToImmutableHashSet(InputPathCanon.Comparer);
            if (_requiresFullScan)
            {
                touched = touched.Union(_rescanPaths);
            }
            return new BaselineSessionSnapshot(_workspacePath, _generation, _publishedAtUtc,
                _documents.Values.ToImmutableArray(), touched, _requiresFullScan);
        }
    }

    internal void BeginLoad(string workspacePath, ILogger logger)
    {
        (CancellationTokenSource? Cancellation, bool Running) retired;
        lock (_gate)
        {
            retired = CloseGenerationUnderLock();
            var canonical = Path.GetFullPath(workspacePath);
            if (!InputPathCanon.Comparer.Equals(_pendingWorkspacePath, canonical))
            {
                _pendingWrites.Clear();
                _pendingObservationGap = false;
            }
            _pendingWorkspacePath = canonical;
        }
        CancelCapture(retired.Cancellation, retired.Running, logger);
    }

    internal void Interrupt(ILogger logger)
    {
        (CancellationTokenSource? Cancellation, bool Running) retired;
        lock (_gate)
        {
            retired = CloseGenerationUnderLock();
        }
        CancelCapture(retired.Cancellation, retired.Running, logger);
    }

    internal void Publish(string workspacePath, Solution solution, DateTime publishedAtUtc, ILogger logger)
    {
        var canonical = Path.GetFullPath(workspacePath);
        lock (_gate)
        {
            // Edit/cached publications share the first inventory; only a real load can add rescan candidates.
            if (InputPathCanon.Comparer.Equals(_workspacePath, canonical) && _pendingWorkspacePath == null)
            {
                return;
            }
        }
        var inventory = Inventory(solution);
        CaptureWork? work;
        lock (_gate)
        {
            if (InputPathCanon.Comparer.Equals(_workspacePath, canonical))
            {
                _rescanPaths.UnionWith(inventory.Select(entry => entry.Key.DocumentPath));
                foreach (var path in _pendingWrites)
                {
                    RecordWriteUnderLock(path);
                }
                _pendingWrites.Clear();
                _pendingWorkspacePath = null;
                _pendingObservationGap = false;
                return;
            }

            InitializeBaselineUnderLock(canonical, publishedAtUtc, inventory);
            foreach (var path in _pendingWrites)
            {
                RecordWriteUnderLock(path);
            }
            _pendingWrites.Clear();
            _pendingWorkspacePath = null;
            _pendingObservationGap = false;
            work = new CaptureWork(solution, inventory, _generation, _publishedAtUtc,
                BeforeReadForTests, BeforeCommitForTests);
            _captureCancellation = work.Cancellation;
            // This completion task contains no Solution; retaining a completed worker task could retain its async locals.
            _captureTask = work.Completion.Task;
        }
        _ = Task.Run(() => CaptureAsync(work, logger));
    }

    internal void RecordWrite(string path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical == null)
        {
            return;
        }
        lock (_gate)
        {
            if (_pendingWorkspacePath != null)
            {
                _pendingWrites.Add(canonical);
            }
            if (_workspacePath != null && (_pendingWorkspacePath == null
                || InputPathCanon.Comparer.Equals(_workspacePath, _pendingWorkspacePath)))
            {
                RecordWriteUnderLock(canonical);
            }
        }
    }

    internal void NoteObservationGap()
    {
        lock (_gate)
        {
            if (_pendingWorkspacePath != null)
            {
                _pendingObservationGap = true;
            }
            if (_workspacePath != null && (_pendingWorkspacePath == null
                || InputPathCanon.Comparer.Equals(_workspacePath, _pendingWorkspacePath)))
            {
                _requiresFullScan = true;
                FreezePendingUnderLock();
            }
        }
    }

    private static void CancelCapture(CancellationTokenSource? cancellation, bool running, ILogger logger)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion can dispose the captured source after it leaves the admission gate.
        }
        if (running)
        {
            logger.LogInformation("Baseline capture cancelled without waiting; its captured solution remains until the worker stops.");
        }
    }

    private (CancellationTokenSource? Cancellation, bool Running) CloseGenerationUnderLock()
    {
        _generation++;
        FreezePendingUnderLock();
        _requiresFullScan |= _workspacePath != null;
        var result = (_captureCancellation, !_captureTask.IsCompleted);
        _captureCancellation = null;
        return result;
    }

    private void InitializeBaselineUnderLock(
        string workspacePath, DateTime publishedAtUtc, ImmutableArray<(BaselineDocumentKey Key, DocumentId Id)> inventory)
    {
        _workspacePath = workspacePath;
        _publishedAtUtc = publishedAtUtc;
        _documents.Clear();
        _pendingByPath.Clear();
        _versions.Clear();
        _touched.Clear();
        _rescanPaths.Clear();
        // Watcher startup may fail before the first publication; preserve that gap only for this load identity.
        // See docs/rapid-tia/epoch-1-project-level/evidence/task-03-review-round-2.md.
        _requiresFullScan = _pendingObservationGap;
        foreach (var entry in inventory)
        {
            _documents.Add(entry.Key, new BaselineDocumentState(entry.Key, BaselineDocumentStatus.Pending, null));
            _rescanPaths.Add(entry.Key.DocumentPath);
            if (!_pendingByPath.TryGetValue(entry.Key.DocumentPath, out var pending))
            {
                pending = new HashSet<BaselineDocumentKey>();
                _pendingByPath.Add(entry.Key.DocumentPath, pending);
            }
            pending.Add(entry.Key);
        }
        if (_requiresFullScan)
        {
            FreezePendingUnderLock();
        }
    }

    private async Task CaptureAsync(CaptureWork work, ILogger logger)
    {
        var outcome = "success";
        try
        {
            foreach (var entry in work.Inventory)
            {
                work.Token.ThrowIfCancellationRequested();
                await CaptureDocumentAsync(work, entry).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (work.Token.IsCancellationRequested)
        {
            outcome = "cancelled";
        }
        catch (Exception)
        {
            outcome = "failure";
            lock (_gate)
            {
                if (_generation == work.Generation)
                {
                    FreezePendingUnderLock();
                    _requiresFullScan = true;
                }
            }
        }
        finally
        {
            // R-15 (docs/rapid-tia/README.md): an interrupted worker may keep the graph only until it actually stops.
            work.Solution = null;
            lock (_gate)
            {
                if (ReferenceEquals(_captureCancellation, work.Cancellation))
                {
                    _captureCancellation = null;
                }
            }
            work.Cancellation.Dispose();
            work.Completion.TrySetResult();
        }
        logger.LogInformation("baseline_capture generation={Generation} outcome={Outcome}", work.Generation, outcome);
    }

    private async Task CaptureDocumentAsync(CaptureWork work, (BaselineDocumentKey Key, DocumentId Id) entry)
    {
        long version;
        lock (_gate)
        {
            version = _versions.GetValueOrDefault(entry.Key.DocumentPath);
            if (!AdmitsUnderLock(work.Generation, entry.Key, version))
            {
                return;
            }
        }
        if (work.BeforeRead != null)
        {
            await work.BeforeRead(entry.Key, work.Token).ConfigureAwait(false);
        }
        lock (_gate)
        {
            if (!AdmitsUnderLock(work.Generation, entry.Key, version))
            {
                return;
            }
        }

        Document? document = null;
        try
        {
            if (UntrustedTimestamp(entry.Key.DocumentPath, work.PublishedAtUtc))
            {
                MarkUnknown(work.Generation, entry.Key);
                return;
            }
            document = work.Solution!.GetDocument(entry.Id);
            var tree = document == null ? null : await document.GetSyntaxTreeAsync(work.Token).ConfigureAwait(false);
            if (tree == null)
            {
                MarkUnknown(work.Generation, entry.Key);
                return;
            }
            var snapshot = BaselineDocumentSnapshotBuilder.Build(tree);
            if (work.BeforeCommit != null)
            {
                await work.BeforeCommit(entry.Key, work.Token).ConfigureAwait(false);
            }
            if (UntrustedTimestamp(entry.Key.DocumentPath, work.PublishedAtUtc))
            {
                MarkUnknown(work.Generation, entry.Key);
                return;
            }
            lock (_gate)
            {
                if (AdmitsUnderLock(work.Generation, entry.Key, version))
                {
                    _documents[entry.Key] = new BaselineDocumentState(entry.Key, BaselineDocumentStatus.Known, snapshot);
                    if (_pendingByPath.TryGetValue(entry.Key.DocumentPath, out var pending))
                    {
                        pending.Remove(entry.Key);
                        if (pending.Count == 0)
                        {
                            _pendingByPath.Remove(entry.Key.DocumentPath);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MarkUnknown(work.Generation, entry.Key);
        }
        finally
        {
            document = null;
        }
    }

    private bool AdmitsUnderLock(long generation, BaselineDocumentKey key, long version) =>
        generation == _generation && _versions.GetValueOrDefault(key.DocumentPath) == version
        && _documents.TryGetValue(key, out var state) && state.Status == BaselineDocumentStatus.Pending;

    private void MarkUnknown(long generation, BaselineDocumentKey key)
    {
        lock (_gate)
        {
            if (generation == _generation)
            {
                RecordWriteUnderLock(key.DocumentPath);
            }
        }
    }

    private void RecordWriteUnderLock(string path)
    {
        _touched.Add(path);
        _versions[path] = _versions.GetValueOrDefault(path) + 1;
        // Only unfinished contexts can become unknown; ordinary writes after capture must not scan the workspace.
        if (_pendingByPath.Remove(path, out var pending))
        {
            foreach (var key in pending)
            {
                _documents[key] = _documents[key] with { Status = BaselineDocumentStatus.Unknown };
            }
        }
    }

    private void FreezePendingUnderLock()
    {
        foreach (var key in _pendingByPath.Values.SelectMany(pending => pending))
        {
            _documents[key] = _documents[key] with { Status = BaselineDocumentStatus.Unknown };
        }
        _pendingByPath.Clear();
    }

    private static bool UntrustedTimestamp(string path, DateTime publishedAtUtc) =>
        !File.Exists(path) || File.GetLastWriteTimeUtc(path) > publishedAtUtc;

    private static ImmutableArray<(BaselineDocumentKey Key, DocumentId Id)> Inventory(Solution solution)
    {
        var result = ImmutableArray.CreateBuilder<(BaselineDocumentKey Key, DocumentId Id)>();
        var projectOccurrences = new Dictionary<(string Path, string Name), int>();
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp)
            .OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            var projectPath = KeyPath(project.FilePath);
            var projectKey = (projectPath, project.Name);
            var projectOccurrence = projectOccurrences.GetValueOrDefault(projectKey);
            projectOccurrences[projectKey] = projectOccurrence + 1;
            var context = project.Name + "#" + projectOccurrence;
            var documents = new Dictionary<(string Path, string Logical), int>();
            foreach (var document in project.Documents.Where(document => document.FilePath != null)
                .OrderBy(document => document.FilePath, StringComparer.Ordinal).ThenBy(document => document.Name, StringComparer.Ordinal))
            {
                var path = KeyPath(document.FilePath);
                var logical = string.Join("/", document.Folders.Append(document.Name));
                var documentKey = (path, logical);
                var occurrence = documents.GetValueOrDefault(documentKey);
                documents[documentKey] = occurrence + 1;
                result.Add((new BaselineDocumentKey(projectPath, context, path, logical, occurrence), document.Id));
            }
        }
        return result.ToImmutable();
    }

    private static string KeyPath(string? path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path) ?? "";
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }

    private sealed class CaptureWork
    {
        internal Solution? Solution { get; set; }
        internal ImmutableArray<(BaselineDocumentKey Key, DocumentId Id)> Inventory { get; }
        internal long Generation { get; }
        internal DateTime PublishedAtUtc { get; }
        internal Func<BaselineDocumentKey, CancellationToken, Task>? BeforeRead { get; }
        internal Func<BaselineDocumentKey, CancellationToken, Task>? BeforeCommit { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token { get; }
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CaptureWork(Solution solution, ImmutableArray<(BaselineDocumentKey Key, DocumentId Id)> inventory,
            long generation, DateTime publishedAtUtc, Func<BaselineDocumentKey, CancellationToken, Task>? beforeRead,
            Func<BaselineDocumentKey, CancellationToken, Task>? beforeCommit)
        {
            Solution = solution;
            Inventory = inventory;
            Generation = generation;
            PublishedAtUtc = publishedAtUtc;
            BeforeRead = beforeRead;
            BeforeCommit = beforeCommit;
            Token = Cancellation.Token;
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<BaselineDocumentKey, BaselineDocumentState> _documents = new();
    private readonly Dictionary<string, HashSet<BaselineDocumentKey>> _pendingByPath = new(InputPathCanon.Comparer);
    private readonly Dictionary<string, long> _versions = new(InputPathCanon.Comparer);
    private readonly HashSet<string> _touched = new(InputPathCanon.Comparer);
    private readonly HashSet<string> _rescanPaths = new(InputPathCanon.Comparer);
    private readonly HashSet<string> _pendingWrites = new(InputPathCanon.Comparer);
    private string? _workspacePath;
    private string? _pendingWorkspacePath;
    private bool _pendingObservationGap;
    private long _generation;
    private DateTime _publishedAtUtc;
    private bool _requiresFullScan;
    private CancellationTokenSource? _captureCancellation;
    private Task _captureTask = Task.CompletedTask;
}
