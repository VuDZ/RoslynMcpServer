using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Models;
using Serilog;

namespace RoslynMcpServer.Services;

public sealed class SolutionManager
{
    /// <summary>Optional <c>RoslynMcp.jsonc</c> settings loaded at process start.</summary>
    public RoslynMcpFileSettings FileSettings { get; }

    /// <summary>Whether the published snapshot came from the config file or an explicit <c>load_workspace</c>.</summary>
    public WorkspaceLoadSource WorkspaceLoadSource { get; private set; } = WorkspaceLoadSource.None;

    /// <summary>True while <see cref="LoadAndPrepareAsync"/> holds the workspace lock for an MSBuild open/prepare.</summary>
    public bool WorkspaceLoadInProgress => _workspaceLoadInProgress;

    /// <summary>Last failed lazy config load report (same style as <c>load_workspace</c>), or null.</summary>
    public string? LastLazyLoadFailureReport { get; private set; }

    public IReadOnlyList<WorkspaceDiagnostic> LastDiagnostics { get; private set; } =
        Array.Empty<WorkspaceDiagnostic>();

    /// <summary>MSBuild <c>Configuration</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedConfiguration { get; private set; }

    /// <summary>
    /// Canonical MSBuild <c>Platform</c> for the last successful <see cref="LoadAsync"/>
    /// (<c>Any CPU</c> → <c>AnyCPU</c>), used as the MSBuildWorkspace global property.
    /// </summary>
    public string? LoadedPlatform { get; private set; }

    /// <summary>
    /// Trimmed platform spelling from the last successful <see cref="LoadAsync"/> without the
    /// <c>Any CPU</c> → <c>AnyCPU</c> alias. Used when inheriting platform for <c>.sln</c>/<c>.slnx</c> CLI.
    /// </summary>
    public string? LoadedPlatformRaw { get; private set; }

    /// <summary>MSBuild <c>TargetFramework</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedTargetFramework { get; private set; }

    /// <summary>
    /// Extra <c>dotnet build</c> args from the last <see cref="LoadAsync"/> (session CLI only; not an MSBuildWorkspace property).
    /// </summary>
    public string? LoadedBuildArgs { get; private set; }

    // Same-assembly tools and the lifecycle test host read these members.
    // InternalsVisibleTo is only RoslynMcpServer.Tests and RoslynMcpServer.LifecycleTestHost.
    // A plugin assembly does not see them; the public members above are its workspace API.

    /// <summary>True when the next <c>load_workspace</c> must skip the in-process graph cache.</summary>
    internal bool ProjectGraphStale => _projectGraphStale;

    internal bool ProjectGraphStaleFromGraphFile => _projectGraphStaleFromGraphFile;

    internal bool ProjectGraphStaleFromComposition => _projectGraphStaleFromComposition;

    /// <summary>True when the last <see cref="LoadAsync"/> reused the existing workspace graph (cached load).</summary>
    internal bool LastLoadWasCacheHit { get; private set; }

    /// <summary>True when the last <see cref="LoadAsync"/> disposed and reopened the MSBuild graph.</summary>
    internal bool LastLoadReopenedGraph { get; private set; }

    /// <summary>True when the last overlay prepare/refresh attempted analyzer file I/O.</summary>
    internal bool LastPrepareAttempted { get; private set; }

    /// <summary>True when the last overlay prepare used the injected prepare-failure seam.</summary>
    internal bool LastPrepareInjectedFailure { get; private set; }

    /// <summary>True when the last refresh failed and a previous compatible mapping was kept as stale.</summary>
    internal bool LastRefreshStale { get; private set; }

    internal int OverlayPrepareCount { get; private set; }

    internal IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> LastShadowCopyResults { get; private set; } =
        Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();

    internal AnalyzerExecutionObservation LastExecutionObservation { get; private set; } =
        AnalyzerExecutionObservation.None;

    internal AnalyzerShadowPublicationPlan? LastPublicationPlan { get; private set; }

    internal SemanticPublicationAdmission PublicationAdmission => _publicationState.Admission;

    internal string? PublicationBanReason => _publicationState.BanReason;

    // Not one overlay-enabled flag. The mapping is the prepared generation,
    // the last-refresh properties describe only the latest prepare, and
    // ShadowCopyAnalyzersEnabled stays set for the session once an overlay is active.
    // LastExecutionObservation is the first-use gate (prepared, rewritten, load failed,
    // executing, or restart required). The mapping is also held across an in-flight write
    // so publication can be reversed without another analyzer-file pass.
    internal bool ShadowCopyAnalyzersEnabled { get; private set; }

    internal string? ShadowCopyRootDirectory { get; private set; }

    internal AnalyzerShadowMapping? AnalyzerShadowMapping { get; private set; }

    internal Guid LoadSessionId { get; private set; }

    internal AnalyzerProvenanceSnapshot? AnalyzerProvenanceSnapshot { get; private set; }

    internal long AnalyzerProvenanceCaptureCount { get; private set; }

    internal AnalyzerProvenanceCaptureFailureMode FailNextAnalyzerProvenanceCapture
    {
        set => _analyzerProvenanceCaptureService.FailureModeForNextCapture = value;
    }

    /// <summary>
    /// When true, the next <see cref="ShadowCopyInSolutionAnalyzerReferencesAsync"/> skips file preparation
    /// (simulates a failed refresh). Document edit / flush / post-apply reapply the existing mapping and do
    /// not consume this seam. Test/host only; production never sets this.
    /// </summary>
    internal bool FailNextOverlayPrepare { get; set; }

    /// <summary>
    /// Test seam invoked after physical load/cache lookup and before opt-in prepare while
    /// <see cref="_workspaceLock"/> is still held.
    /// </summary>
    internal Func<CancellationToken, Task>? AfterPhysicalLoadBeforePrepareAsync { get; set; }

    /// <summary>Test seam: next workspace apply returns false without calling Roslyn.</summary>
    internal bool FailNextTryApplyChanges { get; set; }

    /// <summary>Test seam: throw <see cref="IOException"/> when persisting this path (or any path when <c>*</c>).</summary>
    internal string? FailNextDocumentWritePath { get; set; }

    /// <summary>Test seam: reconciliation of saved texts fails without publishing the candidate.</summary>
    internal bool FailNextReconciliation { get; set; }

    /// <summary>Test seam: cancel after this many successful document writes (0 = disabled).</summary>
    internal int CancelAfterDocumentWrites { get; set; }

    /// <summary>Test seam: drop the captured snapshot before the opt-in publication gate.</summary>
    internal bool DiscardNextProvenanceSnapshot { get; set; }

    /// <summary>Test seam: rewrite the snapshot session id so it no longer matches this load.</summary>
    internal bool AssignForeignSessionToNextCapture { get; set; }

    internal bool HasPublishedSemanticSnapshot => _solution is not null;

    /// <summary>True when a watcher error or a directory rename asked the next flush to re-read every known document.</summary>
    internal bool RefreshAllDocumentsPending => _refreshAllDocuments;

    internal WorkspaceInputSession? DiskWatcherSession => _diskWatcherSession;

    /// <summary>Test seam invoked before session admission, allowing delivery to race with session closure.</summary>
    internal Action? BeforeDiskWatcherCallbackForTests { get; set; }

    /// <summary>Test observer for completed file-change callbacks, including ignored build artifacts.</summary>
    internal Action<FileSystemEventArgs>? AfterDiskWatcherChangeForTests { get; set; }

    internal WorkspaceWriteResult? LastWriteResult { get; private set; }

    internal InProcessAnalyzerAssemblyLoader AnalyzerAssemblyLoader { get; } = new();

    public SolutionManager(
        ILogger<SolutionManager> logger,
        AnalyzerProvenanceCaptureService analyzerProvenanceCaptureService,
        RoslynMcpFileSettings? fileSettings = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(analyzerProvenanceCaptureService);

        _logger = logger;
        _analyzerProvenanceCaptureService = analyzerProvenanceCaptureService;
        FileSettings = fileSettings ?? RoslynMcpFileSettings.Empty;
        _dirtySourcePaths = new ConcurrentDictionary<string, byte>(_pathComparer);
        _selfWriteUntilTicks = new ConcurrentDictionary<string, long>(_pathComparer);
        _missingOnDiskPaths = new ConcurrentDictionary<string, byte>(_pathComparer);
    }

    public async Task<Solution> LoadAsync(string path)
    {
        return await LoadAsync(path, CancellationToken.None);
    }

    public async Task<Solution> LoadAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken,
        string? configuration = null,
        string? platform = null,
        string? targetFramework = null,
        string? buildArgs = null)
    {
        var result = await LoadAndPrepareAsync(
                solutionOrProjectPath,
                shadowCopyInSolutionAnalyzers: false,
                cancellationToken,
                configuration,
                platform,
                targetFramework,
                buildArgs)
            .ConfigureAwait(false);
        return result.Solution;
    }

    /// <summary>
    /// Text-edit write path: preflight, persist the document, apply the cleaned candidate, publish overlay.
    /// Callers must not write the file first — persistence happens after preflight.
    /// </summary>
    public async Task<WorkspaceWriteResult> UpdateDocumentInMemoryAsync(
        string filePath,
        string newText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return RememberWrite(WorkspaceWriteResult.Skipped("empty-path"));
        }

        var fullPath = Path.GetFullPath(filePath);
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return await UpdateDocumentInMemoryUnderLockAsync(fullPath, newText ?? string.Empty, persistToDisk: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<Document?> FindDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fullFilePath = ResolvePathAgainstWorkspace(filePath);
        await EnsureWorkspaceFromConfigAsync(cancellationToken).ConfigureAwait(false);
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            if (_workspace is null)
            {
                return null;
            }

            await FlushDirtyDocumentsUnderLockAsync(cancellationToken);

            // Return only a published snapshot. In particular, never fall back to raw
            // workspace.CurrentSolution while an opt-in load is fail-closed.
            var solution = _solution;
            if (solution is null)
            {
                return null;
            }

            return solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d =>
                    string.Equals(Path.GetFullPath(d.FilePath ?? string.Empty), fullFilePath, _pathComparison));
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Resolves file paths against loaded workspace root when available.
    /// Absolute paths are normalized and returned as-is.
    /// </summary>
    public string ResolvePathAgainstWorkspace(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Path.GetFullPath(filePath ?? string.Empty);
        }

        var trimmed = filePath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var loadedPath = _loadedPath;
        if (!string.IsNullOrWhiteSpace(loadedPath))
        {
            var workspaceBaseDirectory = Path.GetDirectoryName(Path.GetFullPath(loadedPath));
            if (!string.IsNullOrWhiteSpace(workspaceBaseDirectory))
            {
                return Path.GetFullPath(Path.Combine(workspaceBaseDirectory, trimmed));
            }
        }

        return Path.GetFullPath(trimmed);
    }

    public Solution? GetCurrentSolution()
    {
        // Stored snapshot only. Overlay is prepared at load/enable/refresh and reapplied from the in-memory
        // mapping at mutation points (edit / flush / post-apply). This getter does not copy analyzer files,
        // hash generations, or recompute the overlay.
        return _solution;
    }

    /// <summary>
    /// Prepares immutable shadow generations for in-solution analyzer references and stores the mapping
    /// (see <see cref="AnalyzerReferenceShadowCopier"/>). Allowed at load/enable and explicit artifact refresh.
    /// </summary>
    /// <remarks>
    /// This deliberately never calls <see cref="Workspace.TryApplyChanges(Solution)"/> with the rewritten
    /// solution. <see cref="MSBuildWorkspace"/> supports <c>ApplyChangesKind.AddAnalyzerReference</c> /
    /// <c>RemoveAnalyzerReference</c> by editing the backing <c>.csproj</c> on disk — confirmed via the
    /// <c>C:\Scratch\GenRepro</c> repro: applying this way (a) injected a machine-/session-specific temp shadow
    /// path as a new literal <c>&lt;Analyzer Include=...&gt;</c> item, and (b) could not remove the original
    /// <c>ProjectReference OutputItemType="Analyzer"</c>-derived reference (it is synthesized by MSBuild, not a
    /// literal item), leaving both active and making the generator run twice (CS0102/CS0111 on the next real
    /// <c>dotnet build</c>). The rewrite is kept in an in-memory mapping bound to this load session.
    /// Document edit, watcher flush, reconciliation and post-apply reapply that mapping with no analyzer
    /// file I/O. Published generations are not deleted on <see cref="ClearWorkspaceAsync"/>. No-op (empty
    /// result) when no workspace is loaded.
    /// </remarks>
    public async Task<IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult>> ShadowCopyInSolutionAnalyzerReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return PrepareInSolutionAnalyzerReferencesUnderLock();
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Waits for an in-flight load/prepare boundary and returns only its published snapshot.
    /// It never falls back to raw <see cref="Workspace.CurrentSolution"/>.
    /// When no solution is published yet and <c>RoslynMcp.jsonc</c> has <c>workspace-path</c>,
    /// loads via <see cref="LoadAndPrepareAsync"/> first (does not cancel an in-progress load).
    /// </summary>
    public async Task<Solution?> GetPublishedSolutionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureWorkspaceFromConfigAsync(cancellationToken).ConfigureAwait(false);
        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _solution;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Applies queued disk changes and returns the published snapshot under one lock acquisition.
    /// This is the raw published graph (overlay/admission/health). It is not analyzer-sanitized.
    /// </summary>
    public async Task<Solution?> GetPublishedSolutionAfterDiskSyncAsync(CancellationToken cancellationToken = default)
    {
        await EnsureWorkspaceFromConfigAsync(cancellationToken).ConfigureAwait(false);
        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
            return _solution;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Applies queued disk changes and returns a cached snapshot of the published solution with
    /// <c>UnresolvedAnalyzerReference</c> stubs removed. Does not replace the published snapshot —
    /// health, overlay, and admission still see missing analyzer paths.
    /// </summary>
    public async Task<Solution?> GetSanitizedPublishedSolutionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureWorkspaceFromConfigAsync(cancellationToken).ConfigureAwait(false);
        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
            return GetOrCreateSanitizedPublishedSolution();
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// First semantic call with no published solution and a config <c>workspace-path</c>:
    /// same <see cref="LoadAndPrepareAsync"/> path as <c>load_workspace</c>.
    /// Waits for an in-progress load; does not cancel it.
    /// </summary>
    public async Task EnsureWorkspaceFromConfigAsync(CancellationToken cancellationToken = default)
    {
        if (_solution is not null)
        {
            return;
        }

        // Spec: any broken RoslynMcp.jsonc disables lazy load (load_workspace still works;
        // parse failures remain visible in logs / get_mcp_server_info).
        if (FileSettings.ParseFailures.Count > 0)
        {
            return;
        }

        var workspacePath = FileSettings.ResolveWorkspacePathAgainstWorkingDirectory();
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return;
        }

        try
        {
            await LoadAndPrepareAsync(
                    workspacePath,
                    shadowCopyInSolutionAnalyzers: false,
                    cancellationToken,
                    FileSettings.Configuration,
                    FileSettings.Platform,
                    FileSettings.TargetFramework,
                    buildArgs: null,
                    loadSource: WorkspaceLoadSource.ConfigFile)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastLazyLoadFailureReport = FormatLazyLoadFailureReport(workspacePath, ex);
            _logger.LogWarning(ex, "Lazy RoslynMcp.jsonc workspace load failed for {Path}", workspacePath);
        }
    }

    /// <summary>
    /// Returns the cached sanitized snapshot of the current published solution without flushing disk
    /// changes. Used as the retry source when Roslyn 5.9.0 throws on a project checksum.
    /// Acquires <see cref="_workspaceLock"/> (sync <see cref="SemaphoreSlim.Wait()"/> — not a Task).
    /// Must not be called while the caller already holds that lock (<see cref="SemaphoreSlim"/> is not recursive);
    /// <see cref="GetSanitizedPublishedSolutionAsync"/> calls <see cref="GetOrCreateSanitizedPublishedSolution"/> directly.
    /// </summary>
    public Solution? GetSanitizedPublishedSolution()
    {
        _workspaceLock.Wait();
        try
        {
            return GetOrCreateSanitizedPublishedSolution();
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Flushes watcher dirty paths into the in-memory workspace. No-op when nothing changed (O(1)).
    /// </summary>
    public async Task EnsureDiskChangesAppliedAsync(CancellationToken cancellationToken = default)
    {
        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Suppresses watcher-driven re-reads of <paramref name="filePath"/> for a short window after this
    /// process writes the file (avoids reading a torn file). Call before <c>File.WriteAllText</c>.
    /// </summary>
    public void SuppressDiskWatchForPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        _selfWriteUntilTicks[fullPath] = Environment.TickCount64 + SelfWriteSuppressMs;
    }

    /// <summary>
    /// Hint when the project graph or compile membership may be stale. Source <c>.cs</c> of known documents is still synced;
    /// membership and refs need <c>reset_workspace</c> + <c>load_workspace</c> unless the next load skips cache.
    /// </summary>
    public string? GetProjectGraphStaleHint()
    {
        if (!_projectGraphStale)
        {
            return null;
        }

        var graphFile = _projectGraphStaleFromGraphFile;
        var composition = _projectGraphStaleFromComposition;
        if (graphFile && composition)
        {
            return ProjectGraphFileStaleHint
                + Environment.NewLine
                + Environment.NewLine
                + ProjectGraphCompositionStaleHint;
        }

        if (composition)
        {
            return ProjectGraphCompositionStaleHint;
        }

        return ProjectGraphFileStaleHint;
    }

    /// <summary>
    /// Marks a <c>.cs</c> path as unrepresentable (new file outside the loaded snapshot).
    /// Thread-safe; may be called without <see cref="_workspaceLock"/>.
    /// </summary>
    public void NoteUnrepresentableSourcePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !WorkspaceDiskPathFilter.IsCSharpSource(filePath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            return;
        }

        MarkProjectGraphStaleFromComposition();
        _logger.LogWarning("Unrepresentable source path (composition unknown): {Path}", fullPath);
    }

    public string WithDiskSyncNotes(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var hint = GetProjectGraphStaleHint();
        if (hint is null)
        {
            return body;
        }

        return body + Environment.NewLine + Environment.NewLine + hint;
    }

    public string? GetLoadedWorkspacePath()
    {
        return _loadedPath;
    }

    public string? GetLoadedWorkspaceDirectory()
    {
        var loadedPath = _loadedPath;
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(loadedPath));
    }

    /// <summary>
    /// Overlay-derived apply: preflight and exact inverse before any document write,
    /// then persist, TryApplyChanges, reconcile, and publish the prepared mapping.
    /// Caller must not hold <see cref="_workspaceLock"/> (this method acquires it).
    /// </summary>
    public async Task<WorkspaceWriteResult> ApplySolutionChangesToDiskAsync(
        Solution oldSolution,
        Solution newSolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldSolution);
        ArgumentNullException.ThrowIfNull(newSolution);

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return await ApplyWorkspaceWriteUnderLockAsync(
                    newSolution,
                    oldSolution,
                    ResolveOperationContext(oldSolution),
                    persistDocuments: true,
                    alreadyOnDisk: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Disposes the active <see cref="MSBuildWorkspace"/> and clears cached solution state so the next
    /// <see cref="LoadAsync"/> rebuilds from disk (e.g. after an external <c>dotnet build</c>).
    /// </summary>
    public async Task ClearWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            StopDiskWatcherUnderLock();
            _dirtySourcePaths.Clear();
            _selfWriteUntilTicks.Clear();
            _missingOnDiskPaths.Clear();
            _refreshAllDocuments = false;
            ClearProjectGraphStale();
            _workspace?.Dispose();
            _workspace = null;
            _solution = null;
            _lastPublishedWriteContext = null;
            LastWriteResult = null;
            NoteRawWorkspaceRevision();
            ShadowCopyAnalyzersEnabled = false;
            ShadowCopyRootDirectory = null;
            AnalyzerShadowMapping = null;
            AnalyzerProvenanceSnapshot = null;
            LoadSessionId = Guid.Empty;
            LastRefreshStale = false;
            LastExecutionObservation = AnalyzerExecutionObservation.None;
            LastPublicationPlan = null;
            _publicationState = SemanticPublicationState.None;
            _loadedPath = null;
            LoadedConfiguration = null;
            LoadedPlatform = null;
            LoadedPlatformRaw = null;
            LoadedTargetFramework = null;
            LoadedBuildArgs = null;
            WorkspaceLoadSource = WorkspaceLoadSource.None;
            LastLazyLoadFailureReport = null;
            LastDiagnostics = Array.Empty<WorkspaceDiagnostic>();
            _logger.LogInformation("Roslyn workspace cleared (MSBuildWorkspace disposed).");
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    internal string FormatNoPublishedSolutionMessage(string? leadingSentence = null)
    {
        if (!string.IsNullOrWhiteSpace(LastLazyLoadFailureReport))
        {
            if (string.IsNullOrWhiteSpace(leadingSentence))
            {
                return LastLazyLoadFailureReport;
            }

            return leadingSentence.TrimEnd() + Environment.NewLine + Environment.NewLine + LastLazyLoadFailureReport;
        }

        if (_workspace is not null && _publicationState.IsUnavailable)
        {
            return WorkspaceLoadGuidance.FormatSemanticWorkspaceUnavailableMessage(
                leadingSentence,
                AnalyzerProvenanceSnapshot?.Status.ToString(),
                _publicationState.BanReason,
                overlayAllowed: false,
                captureReused: LastLoadWasCacheHit);
        }

        return WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage(leadingSentence);
    }

    /// <summary>
    /// First-use observation: maps a load/execution failure to project, generator,
    /// generation, and missing/conflicting dependency. Loading stays lazy until this
    /// or a semantic compilation runs.
    /// </summary>
    internal AnalyzerExecutionObservation ObserveAnalyzerExecution(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        LastExecutionObservation = AnalyzerExecutionGate.ObserveFirstUse(
            project,
            LastExecutionObservation,
            AnalyzerAssemblyLoader);
        return LastExecutionObservation;
    }

    internal IReadOnlyList<string> GetPendingDirtySourcePaths() => _dirtySourcePaths.Keys.ToArray();

    internal Solution? GetWorkspaceCurrentSolution() => _workspace?.CurrentSolution;

    internal async Task<WorkspaceLoadPreparationResult> LoadAndPrepareAsync(
        string solutionOrProjectPath,
        bool shadowCopyInSolutionAnalyzers,
        CancellationToken cancellationToken,
        string? configuration = null,
        string? platform = null,
        string? targetFramework = null,
        string? buildArgs = null,
        WorkspaceLoadSource loadSource = WorkspaceLoadSource.ExplicitLoad)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            throw new ArgumentException("Solution or project path cannot be empty.", nameof(solutionOrProjectPath));
        }

        var fullPath = Path.GetFullPath(solutionOrProjectPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Solution or project file not found.", fullPath);
        }

        var buildArgsPassed = !string.IsNullOrWhiteSpace(buildArgs);

        var normalizedConfiguration = DotNetConfigurationArguments.Normalize(configuration, nameof(configuration));
        var platformRaw = DotNetConfigurationArguments.Normalize(platform, nameof(platform));
        var normalizedPlatform = DotNetConfigurationArguments.NormalizePlatform(platform);
        var normalizedTargetFramework = DotNetConfigurationArguments.Normalize(targetFramework, nameof(targetFramework));
        var normalizedBuildArgs = DotNetBuildArguments.Normalize(buildArgs);

        await _workspaceLock.WaitAsync(cancellationToken);
        _workspaceLoadInProgress = true;
        try
        {
            // Cancel and Release can race: SemaphoreSlim may grant the waiter after its token
            // is already cancelled. Do not run this load; the in-progress load keeps its own token.
            cancellationToken.ThrowIfCancellationRequested();

            var fileConfiguration = DotNetConfigurationArguments.Normalize(
                FileSettings.Configuration,
                nameof(FileSettings.Configuration));
            var filePlatformRaw = DotNetConfigurationArguments.Normalize(
                FileSettings.Platform,
                nameof(FileSettings.Platform));
            var filePlatform = DotNetConfigurationArguments.NormalizePlatform(FileSettings.Platform);
            var fileTargetFramework = DotNetConfigurationArguments.Normalize(
                FileSettings.TargetFramework,
                nameof(FileSettings.TargetFramework));

            // Passed overrides loaded; omitted keeps loaded; when nothing loaded yet, fill from file.
            var effectiveConfiguration = normalizedConfiguration ?? LoadedConfiguration ?? fileConfiguration;
            var effectivePlatform = normalizedPlatform ?? LoadedPlatform ?? filePlatform;
            var effectivePlatformRaw = !string.IsNullOrWhiteSpace(platform)
                ? platformRaw
                : LoadedPlatformRaw ?? filePlatformRaw ?? platformRaw;
            var effectiveTargetFramework = normalizedTargetFramework
                ?? LoadedTargetFramework
                ?? fileTargetFramework;
            var effectiveBuildArgs = buildArgsPassed ? normalizedBuildArgs : LoadedBuildArgs;

            var publishedBeforeBoundary = _solution;
            Solution solution;
            try
            {
                solution = await LoadCoreAsync(
                        fullPath,
                        passedConfiguration: normalizedConfiguration,
                        passedPlatform: normalizedPlatform,
                        passedTargetFramework: normalizedTargetFramework,
                        effectiveConfiguration: effectiveConfiguration,
                        effectivePlatform: effectivePlatform,
                        effectivePlatformRaw: effectivePlatformRaw,
                        effectiveTargetFramework: effectiveTargetFramework,
                        effectiveBuildArgs: effectiveBuildArgs,
                        applyBuildArgs: buildArgsPassed,
                        publishLoadedSolution: !shadowCopyInSolutionAnalyzers,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (shadowCopyInSolutionAnalyzers)
                {
                    RestoreSafePublishedSnapshotAfterOptInFailure();
                }

                throw;
            }

            if (_solution is not null)
            {
                WorkspaceLoadSource = loadSource;
                LastLazyLoadFailureReport = null;
            }

            if (!shadowCopyInSolutionAnalyzers)
            {
                return new WorkspaceLoadPreparationResult(solution, LastShadowCopyResults);
            }

            if (HasBlockingLoadFailure(solution))
            {
                EnterBannedPublication(_workspace!.CurrentSolution, "blocking-load-failure");
                _solution = null;
                return new WorkspaceLoadPreparationResult(solution, Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>());
            }

            ApplyCaptureTestSeams();
            var unsuitableCapture = AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
                AnalyzerProvenanceSnapshot,
                LoadSessionId);
            if (unsuitableCapture is not null)
            {
                EnterUnavailablePublication(unsuitableCapture);
                return new WorkspaceLoadPreparationResult(
                    _workspace!.CurrentSolution,
                    Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>());
            }

            if (LastLoadWasCacheHit)
            {
                _solution = publishedBeforeBoundary;
            }

            try
            {
                var boundarySeam = AfterPhysicalLoadBeforePrepareAsync;
                if (boundarySeam is not null)
                {
                    await boundarySeam(cancellationToken).ConfigureAwait(false);
                }

                var results = PrepareInSolutionAnalyzerReferencesUnderLock();
                if (_publicationState.Admission is SemanticPublicationAdmission.None
                    || (!_publicationState.AllowsOverlay
                        && AnalyzerShadowMapping is not { HasAnyApplied: true }))
                {
                    SetFailClosedPublishedSolution(
                        _workspace!.CurrentSolution,
                        LastExecutionObservation.Reason ?? "opt-in-prepare-not-enabled");
                }

                return new WorkspaceLoadPreparationResult(_workspace!.CurrentSolution, results);
            }
            catch
            {
                RestoreSafePublishedSnapshotAfterOptInFailure();
                throw;
            }
        }
        finally
        {
            _workspaceLoadInProgress = false;
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Waits until <paramref name="filePath"/> appears in the watcher dirty set, or <paramref name="timeout"/> elapses.
    /// Does not flush. Distinguishes undelivered FSW events from a later flush failure.
    /// </summary>
    internal async Task<DirtySourceWaitResult> WaitForDirtySourceAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new DirtySourceWaitResult(Delivered: false, TimedOut: true, Elapsed: TimeSpan.Zero, PendingCount: _dirtySourcePaths.Count);
        }

        var fullPath = Path.GetFullPath(filePath);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_dirtySourcePaths.ContainsKey(fullPath))
            {
                return new DirtySourceWaitResult(
                    Delivered: true,
                    TimedOut: false,
                    Elapsed: started.Elapsed,
                    PendingCount: _dirtySourcePaths.Count);
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return new DirtySourceWaitResult(
            Delivered: false,
            TimedOut: true,
            Elapsed: started.Elapsed,
            PendingCount: _dirtySourcePaths.Count);
    }

    /// <summary>
    /// Test seam: next flush re-reads every known document (watcher-error / directory-rename path).
    /// </summary>
    internal void RequestRefreshAllDocumentsForTests()
    {
        _refreshAllDocuments = true;
    }

    /// <summary>Test seam: the watcher <c>Error</c> callback, including a logger that throws.</summary>
    internal void NotifyDiskWatcherError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (_diskWatcherSession is { } session)
        {
            OnDiskWatcherError(session, new ErrorEventArgs(exception));
        }
    }

    /// <summary>Test seam: the watcher <c>Renamed</c> callback for a directory that exists on disk.</summary>
    internal void NotifyDiskWatcherDirectoryRename(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (_diskWatcherSession is { } session)
        {
            OnDiskWatcherRenamed(
                session,
                new RenamedEventArgs(WatcherChangeTypes.Renamed, directoryPath, name: null, oldName: null));
        }
    }

    /// <summary>Creates a session without physical watchers for deterministic callback tests.</summary>
    internal WorkspaceInputSession StartDiskWatcherSessionForTests(IEnumerable<string?> projectFilePaths)
    {
        StopDiskWatcherUnderLock();
        var session = new WorkspaceInputSession(projectFilePaths);
        _diskWatcherSession = session;
        return session;
    }

    internal void NotifyDiskWatcherChange(WorkspaceInputSession session, FileSystemEventArgs args)
        => OnDiskWatcherChanged(session, args);

    internal void NotifyDiskWatcherRename(WorkspaceInputSession session, RenamedEventArgs args)
        => OnDiskWatcherRenamed(session, args);

    internal void NotifyDiskWatcherError(WorkspaceInputSession session, Exception exception)
        => OnDiskWatcherError(session, new ErrorEventArgs(exception));

    /// <summary>
    /// Session CLI extras are not part of the MSBuildWorkspace cache key.
    /// Always replace so a second <c>load_workspace</c> can change or clear them without reopening the solution.
    /// </summary>
    internal void ApplySessionBuildArgs(string? buildArgs) => LoadedBuildArgs = buildArgs;

    /// <summary>
    /// Directories to watch (and later to search) for a loaded workspace: directory of
    /// <paramref name="loadedFilePath"/> union directories of <paramref name="projectFilePaths"/>,
    /// with nested duplicates removed (if A contains B, keep A). Does not invent ancestor
    /// <c>Directory.Build.props</c> roots and does not LCA-merge (Unix absolute paths stay rooted).
    /// </summary>
    internal static IReadOnlyList<string> ComputeWatchRoots(
        string? loadedFilePath,
        IEnumerable<string?>? projectFilePaths)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var directories = new HashSet<string>(comparer);
        TryAddWatchDirectory(directories, loadedFilePath);
        if (projectFilePaths is not null)
        {
            foreach (var projectFilePath in projectFilePaths)
            {
                TryAddWatchDirectory(directories, projectFilePath);
            }
        }

        if (directories.Count == 0)
        {
            return Array.Empty<string>();
        }

        var roots = new List<string>();
        foreach (var directory in directories)
        {
            var nested = false;
            foreach (var other in directories)
            {
                if (comparer.Equals(directory, other))
                {
                    continue;
                }

                if (IsStrictSubdirectory(directory, other, comparison))
                {
                    nested = true;
                    break;
                }
            }

            if (!nested)
            {
                roots.Add(directory);
            }
        }

        roots.Sort(comparer);
        return roots;
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> PrepareInSolutionAnalyzerReferencesUnderLock()
    {
        var workspace = _workspace;
        var loadedPath = _loadedPath;
        if (workspace is null || loadedPath is null)
        {
            return Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
        }

        var shadowRoot = AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(loadedPath);
        LastPrepareAttempted = true;
        OverlayPrepareCount++;

        if (FailNextOverlayPrepare)
        {
            FailNextOverlayPrepare = false;
            LastPrepareInjectedFailure = true;
            return CompleteFailedPrepare(
                workspace.CurrentSolution,
                shadowRoot,
                "injected-prepare-failure");
        }

        LastPrepareInjectedFailure = false;
        var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
            workspace.CurrentSolution,
            shadowRoot,
            AnalyzerAssemblyLoader,
            AnalyzerShadowMapping,
            LoadSessionId,
            loadedPath,
            AnalyzerProvenanceSnapshot);
        return CompletePrepare(workspace.CurrentSolution, shadowRoot, prepared);
    }

    /// <summary>
    /// Reapplies the session publication policy on top of <paramref name="solution"/> with no analyzer
    /// file I/O, inspector, or provenance rediscovery. Admission and the excluded-reference set were
    /// decided at the load/prepare boundary.
    /// </summary>
    private Solution PublishInMemorySolution(Solution solution)
    {
        return _publicationState.Admission switch
        {
            SemanticPublicationAdmission.NoOverlay => solution,
            SemanticPublicationAdmission.AllowedMapping => SemanticPublicationState.ApplyExcludedReferences(
                ApplyShadowCopyOverlayIfEnabled(solution),
                _publicationState.ExcludedReferences),
            SemanticPublicationAdmission.Banned => SemanticPublicationState.ApplyExcludedReferences(
                solution,
                _publicationState.ExcludedReferences),
            SemanticPublicationAdmission.Unavailable => solution,
            _ => SemanticPublicationState.ApplyExcludedReferences(
                solution,
                _publicationState.ExcludedReferences),
        };
    }

    private Solution ApplyShadowCopyOverlayIfEnabled(Solution solution)
    {
        var mapping = AnalyzerShadowMapping;
        if (!ShadowCopyAnalyzersEnabled
            || mapping is null
            || mapping.SessionId != LoadSessionId)
        {
            return solution;
        }

        if (!string.IsNullOrWhiteSpace(mapping.LoadedPath)
            && !string.IsNullOrWhiteSpace(_loadedPath)
            && !string.Equals(mapping.LoadedPath, _loadedPath, _pathComparison))
        {
            return solution;
        }

        return mapping.Apply(solution, AnalyzerAssemblyLoader);
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> CompletePrepare(
        Solution workspaceSolution,
        string shadowRoot,
        AnalyzerShadowPrepareOutcome prepared)
    {
        var plan = AnalyzerShadowPublicationPlanner.Evaluate(
            prepared,
            AnalyzerAssemblyLoader,
            restartBanLatched: IsRestartBanLatched());
        ApplyPublicationPlan(workspaceSolution, shadowRoot, plan, prepared.Results);
        return LastShadowCopyResults;
    }

    private AnalyzerExecutionObservation EvaluatePreparedMapping(AnalyzerShadowMapping mapping)
    {
        var prepared = new AnalyzerShadowPrepareOutcome(
            mapping,
            AnalyzerReferenceShadowCopier.ToRewriteResults(mapping),
            RefreshSucceeded: mapping.Entries.All(e => !e.StaleGeneration),
            UsedPreviousMappingAsStale: mapping.Entries.Any(e => e.StaleGeneration),
            FailureSummary: null);
        return AnalyzerShadowPublicationPlanner.Evaluate(
            prepared,
            AnalyzerAssemblyLoader,
            restartBanLatched: IsRestartBanLatched()).Gate;
    }

    private void ApplyPublicationPlan(
        Solution workspaceSolution,
        string shadowRoot,
        AnalyzerShadowPublicationPlan plan,
        IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ungatedResults)
    {
        LastPublicationPlan = plan;
        LastExecutionObservation = plan.Gate;
        LastRefreshStale = !plan.RefreshComplete || plan.StaleCount > 0;
        LastShadowCopyResults = AnalyzerShadowPublicationPlanner.ToRewriteResults(ungatedResults, plan);
        ShadowCopyRootDirectory = shadowRoot;
        AnalyzerShadowMapping = plan.Kind == AnalyzerShadowPublicationKind.Unavailable
            ? null
            : plan.Mapping;
        ShadowCopyAnalyzersEnabled = plan.OverlayEnabled;

        _publicationState = plan.Kind switch
        {
            AnalyzerShadowPublicationKind.Allowed => SemanticPublicationState.Allow(plan.Exclusions),
            AnalyzerShadowPublicationKind.Banned => SemanticPublicationState.Banned(plan.Reason, plan.Exclusions),
            AnalyzerShadowPublicationKind.Unavailable => SemanticPublicationState.Unavailable(plan.Reason),
            _ => _publicationState,
        };

        SetPublishedSolution(workspaceSolution);
    }

    private bool IsRestartBanLatched()
    {
        return _publicationState.IsBanned
            && (LastExecutionObservation.RequiresRestart
                || (!string.IsNullOrWhiteSpace(_publicationState.BanReason)
                    && _publicationState.BanReason.Contains(
                        "restart",
                        StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> CompleteFailedPrepare(
        Solution workspaceSolution,
        string shadowRoot,
        string reason)
    {
        LastExecutionObservation = new AnalyzerExecutionObservation
        {
            Status = IsRestartBanLatched()
                ? AnalyzerExecutionStatus.RestartRequired
                : AnalyzerExecutionStatus.LoadFailed,
            HighestStage = IsRestartBanLatched()
                ? AnalyzerPreparationStage.Prepared
                : AnalyzerPreparationStage.LoadFailed,
            Reason = IsRestartBanLatched()
                ? (_publicationState.BanReason ?? AnalyzerLoaderContract.RestartRequiredReason)
                : reason,
            ProjectName = AnalyzerShadowMapping?.Entries.FirstOrDefault()?.ProjectName,
            GeneratorName = AnalyzerShadowMapping?.Entries.FirstOrDefault()?.MatchedProjectName,
            GenerationId = AnalyzerShadowMapping?.Entries.FirstOrDefault()?.GenerationId,
            Action = IsRestartBanLatched() ? AnalyzerLoaderContract.RestartAction : null,
        };

        if (IsRestartBanLatched())
        {
            LastRefreshStale = true;
            ShadowCopyRootDirectory = shadowRoot;
            ShadowCopyAnalyzersEnabled = false;
            if (LastPublicationPlan is { } previousPlan)
            {
                LastPublicationPlan = previousPlan with
                {
                    AppliedCount = 0,
                    OverlayEnabled = false,
                    RefreshComplete = false,
                    Kind = AnalyzerShadowPublicationKind.Banned,
                    Admission = SemanticPublicationAdmission.Banned,
                    Reason = LastExecutionObservation.Reason,
                };
            }

            SetPublishedSolution(workspaceSolution);
            return LastShadowCopyResults;
        }

        if (AnalyzerShadowMapping is { HasAnyApplied: true } previous)
        {
            var stale = previous.WithStale(reason, AnalyzerReferenceReasonCodes.PreparationFailure);
            var preservedExclusions = _publicationState.ExcludedReferences;
            AnalyzerShadowMapping = stale;
            LastShadowCopyResults = AnalyzerReferenceShadowCopier.ToRewriteResults(stale);
            LastRefreshStale = true;
            ShadowCopyAnalyzersEnabled = true;
            ShadowCopyRootDirectory = shadowRoot;
            _publicationState = SemanticPublicationState.Allow(preservedExclusions);
            LastPublicationPlan = new AnalyzerShadowPublicationPlan(
                AnalyzerShadowPublicationKind.Allowed,
                SemanticPublicationAdmission.AllowedMapping,
                stale,
                preservedExclusions,
                LastExecutionObservation,
                OverlayEnabled: true,
                RefreshComplete: false,
                PreparedCount: 0,
                AppliedCount: stale.Entries.Count(e => e.Applied),
                StaleCount: stale.Entries.Count(e => e.StaleGeneration),
                BlockedCount: preservedExclusions.Count,
                Reason: "stale-generation: " + reason);
            SetPublishedSolution(workspaceSolution);
            return LastShadowCopyResults;
        }

        LastShadowCopyResults = Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
        LastRefreshStale = false;
        SetFailClosedPublishedSolution(workspaceSolution, reason);
        return LastShadowCopyResults;
    }

    private void SetPublishedSolution(Solution workspaceSolution)
    {
        if (_publicationState.WithholdsSnapshot)
        {
            _solution = null;
            _lastPublishedWriteContext = CreateVerifiedWriteContext(null, _workspace?.CurrentSolution);
            return;
        }

        var overlay = PublishInMemorySolution(workspaceSolution);
        SetPublishedSnapshot(overlay);
    }

    private void EnterBannedPublication(Solution workspaceSolution, string reason)
    {
        var excluded = SemanticPublicationState.CaptureFromInSolutionReferences(
            workspaceSolution,
            AnalyzerProvenanceSnapshot,
            LoadSessionId);
        _publicationState = SemanticPublicationState.Banned(reason, excluded);
    }

    private void EnterUnavailablePublication(string reason)
    {
        ShadowCopyAnalyzersEnabled = false;
        AnalyzerShadowMapping = null;
        LastShadowCopyResults = Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
        _publicationState = SemanticPublicationState.Unavailable(reason);
        _solution = null;
        _lastPublishedWriteContext = CreateVerifiedWriteContext(null, _workspace?.CurrentSolution);
        _logger.LogWarning(
            "Opt-in semantic snapshot withheld: {Reason} capture={CaptureStatus} session={SessionId} cacheHit={CacheHit}",
            reason,
            AnalyzerProvenanceSnapshot?.Status.ToString() ?? "missing",
            LoadSessionId,
            LastLoadWasCacheHit);
    }

    private void ApplyCaptureTestSeams()
    {
        if (DiscardNextProvenanceSnapshot)
        {
            DiscardNextProvenanceSnapshot = false;
            AnalyzerProvenanceSnapshot = null;
        }

        if (AssignForeignSessionToNextCapture && AnalyzerProvenanceSnapshot is not null)
        {
            AssignForeignSessionToNextCapture = false;
            AnalyzerProvenanceSnapshot = AnalyzerProvenanceSnapshot with
            {
                LoadSessionId = Guid.NewGuid(),
            };
        }
    }

    private void SetFailClosedPublishedSolution(Solution workspaceSolution, string? reason = null)
    {
        var unsuitable = AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
            AnalyzerProvenanceSnapshot,
            LoadSessionId);
        if (unsuitable is not null)
        {
            EnterUnavailablePublication(unsuitable);
            return;
        }

        ShadowCopyAnalyzersEnabled = false;
        EnterBannedPublication(
            workspaceSolution,
            reason ?? LastExecutionObservation.Reason ?? "opt-in-publication-banned");
        LastPublicationPlan = new AnalyzerShadowPublicationPlan(
            AnalyzerShadowPublicationKind.Banned,
            SemanticPublicationAdmission.Banned,
            AnalyzerShadowMapping
                ?? new AnalyzerShadowMapping(LoadSessionId, _loadedPath, Array.Empty<AnalyzerShadowReferenceEntry>()),
            _publicationState.ExcludedReferences,
            LastExecutionObservation,
            OverlayEnabled: false,
            RefreshComplete: false,
            PreparedCount: 0,
            AppliedCount: 0,
            StaleCount: 0,
            BlockedCount: _publicationState.ExcludedReferences.Count,
            Reason: _publicationState.BanReason);
        SetPublishedSnapshot(PublishInMemorySolution(workspaceSolution));
    }

    private void RestoreSafePublishedSnapshotAfterOptInFailure()
    {
        if (_workspace is null)
        {
            _solution = null;
            _publicationState = SemanticPublicationState.Unavailable("opt-in-boundary-failed");
            return;
        }

        if (_publicationState.AllowsOverlay
            && AnalyzerShadowMapping is { HasAnyApplied: true })
        {
            SetPublishedSolution(_workspace.CurrentSolution);
            return;
        }

        var unsuitable = AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(
            AnalyzerProvenanceSnapshot,
            LoadSessionId);
        if (unsuitable is not null)
        {
            EnterUnavailablePublication(unsuitable);
            return;
        }

        SetFailClosedPublishedSolution(
            _workspace.CurrentSolution,
            LastExecutionObservation.Reason ?? "opt-in-boundary-failed");
    }

    private void SetPublishedSnapshot(Solution overlay)
    {
        _solution = overlay;
        var context = CreateVerifiedWriteContext(overlay, _workspace?.CurrentSolution);
        _lastPublishedWriteContext = context;
        try
        {
            _operationContexts.Add(overlay, context);
        }
        catch (ArgumentException)
        {
        }
    }

    private WorkspaceWriteOperationContext CreateVerifiedWriteContext(
        Solution? publishedBase,
        Solution? rawWorkspace)
    {
        return WorkspaceWriteOperationContext.Verified(
            LoadSessionId,
            _loadedPath,
            AnalyzerShadowMapping,
            publishedBase,
            rawWorkspace,
            _rawWorkspaceRevision,
            ShadowCopyAnalyzersEnabled,
            _publicationState.Admission,
            _publicationState.ExcludedReferences);
    }

    private WorkspaceWriteFreshnessState CurrentWriteFreshness(Solution rawWorkspace)
    {
        return new WorkspaceWriteFreshnessState(
            LoadSessionId,
            _loadedPath,
            AnalyzerShadowMapping,
            ShadowCopyAnalyzersEnabled,
            _rawWorkspaceRevision,
            rawWorkspace,
            _solution,
            _publicationState.Admission,
            _publicationState.ExcludedReferences);
    }

    private void NoteRawWorkspaceRevision()
    {
        _rawWorkspaceRevision++;
    }

    private WorkspaceWriteOperationContext ResolveOperationContext(Solution? oldSolution)
    {
        if (oldSolution is not null && _operationContexts.TryGetValue(oldSolution, out var stamped))
        {
            return stamped;
        }

        if (oldSolution is not null
            && _lastPublishedWriteContext is { IsVerified: true } last
            && (ReferenceEquals(oldSolution, last.BaseSnapshot)
                || ReferenceEquals(oldSolution, _solution)))
        {
            return last;
        }

        return WorkspaceWriteOperationContext.Unverified;
    }

    private WorkspaceWriteResult RememberWrite(WorkspaceWriteResult result)
    {
        LastWriteResult = result;
        return result;
    }

    private bool TryApplyWorkspaceChanges(Workspace workspace, Solution cleaned)
    {
        if (FailNextTryApplyChanges)
        {
            FailNextTryApplyChanges = false;
            _logger.LogWarning("TryApplyChanges injected failure (test seam).");
            return false;
        }

        if (!workspace.TryApplyChanges(cleaned))
        {
            return false;
        }

        NoteRawWorkspaceRevision();
        return true;
    }

    private static string FormatLazyLoadFailureReport(string path, Exception ex)
    {
        if (ex is RoslynMsBuildBuildHostException hostEx)
        {
            return hostEx.Message;
        }

        if (WorkspaceLoadGuidance.IsRoslynMsBuildBuildHostFailure(ex))
        {
            return WorkspaceLoadGuidance.FormatRoslynMsBuildBuildHostFailureMessage(path);
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Failed");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{path}`");
        sb.AppendLine($"- **Source:** `RoslynMcp.jsonc` (lazy load)");
        sb.AppendLine();
        sb.AppendLine("### Errors");
        sb.AppendLine($"- {ex.Message}");
        sb.Append(MsBuildEnvironmentInfo.FormatMarkdownSection());
        return sb.ToString();
    }

    private Solution? GetOrCreateSanitizedPublishedSolution()
    {
        var raw = _solution;
        if (raw is null)
        {
            return null;
        }

        if (ReferenceEquals(_sanitizedPublishedSolutionSource, raw))
        {
            return _sanitizedPublishedSolution;
        }

        var (sanitized, removed) = WorkspaceAnalyzerSanitizer.RemoveUnresolvedAnalyzers(raw);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Removed {Removed} unresolved analyzer reference(s) from a cached search snapshot (Roslyn 5.9.0 project-checksum crash workaround). Published solution is unchanged.",
                removed);
        }

        _sanitizedPublishedSolutionSource = raw;
        _sanitizedPublishedSolution = sanitized;
        return sanitized;
    }

    /// <summary>
    /// Must be called with <see cref="_workspaceLock"/> held.
    /// </summary>
    private async Task<WorkspaceWriteResult> UpdateDocumentInMemoryUnderLockAsync(
        string filePath,
        string newText,
        bool persistToDisk,
        CancellationToken cancellationToken)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            _logger.LogDebug("Skip in-memory document update: no workspace loaded ({Path}).", filePath);
            return RememberWrite(WorkspaceWriteResult.Skipped("no-workspace"));
        }

        var fullPath = Path.GetFullPath(filePath);
        var documentId = FindDocumentIdForPath(workspace.CurrentSolution, fullPath, _pathComparison);
        if (documentId is null)
        {
            _logger.LogDebug("Skip in-memory document update: file not part of loaded workspace ({Path}).", fullPath);
            return RememberWrite(WorkspaceWriteResult.Skipped("not-in-workspace"));
        }

        if (IsMissingOnDisk(fullPath))
        {
            _logger.LogWarning(
                "Skip persist of missing-on-disk path {Path} until workspace reload.",
                fullPath);
            return RememberWrite(WorkspaceWriteResult.Skipped("missing-on-disk"));
        }

        var baseSolution = workspace.CurrentSolution;
        var existingDocument = baseSolution.GetDocument(documentId);
        var existingText = existingDocument is null
            ? null
            : await existingDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
        // Preserve the file's BOM state: MSBuildWorkspace rewrites the document with this encoding on apply.
        var writeEncoding = SourceTextEncoding.ResolveForWrite(existingText, candidate: null, fullPath);
        var candidate = baseSolution.WithDocumentText(
            documentId,
            SourceText.From(newText, writeEncoding));
        var context = CreateVerifiedWriteContext(_solution, baseSolution);
        IReadOnlyList<(string Path, string Text)>? alreadyOnDisk = persistToDisk
            ? null
            : [(fullPath, newText)];
        return await ApplyWorkspaceWriteUnderLockAsync(
                candidate,
                baseSolution,
                context,
                persistDocuments: persistToDisk,
                alreadyOnDisk,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WorkspaceWriteResult> ApplyWorkspaceWriteUnderLockAsync(
        Solution candidate,
        Solution? baseForDocuments,
        WorkspaceWriteOperationContext operationContext,
        bool persistDocuments,
        IReadOnlyList<(string Path, string Text)>? alreadyOnDisk,
        CancellationToken cancellationToken)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            return RememberWrite(WorkspaceWriteResult.Skipped("no-workspace"));
        }

        var preflight = WorkspaceWriteBoundary.Preflight(
            candidate,
            workspace.CurrentSolution,
            operationContext,
            CurrentWriteFreshness(workspace.CurrentSolution),
            AnalyzerAssemblyLoader,
            baseForDocuments);
        if (!preflight.Accepted || preflight.CleanedCandidate is null)
        {
            _logger.LogWarning(
                "Workspace write preflight rejected: {Reason}",
                preflight.Reason);
            return RememberWrite(WorkspaceWriteResult.PreflightRejected(preflight.Reason ?? "preflight-rejected"));
        }

        var cleaned = preflight.CleanedCandidate;
        var saved = new List<string>();
        var savedTexts = new List<(string Path, string Text)>();
        if (alreadyOnDisk is not null)
        {
            foreach (var item in alreadyOnDisk)
            {
                saved.Add(item.Path);
                savedTexts.Add(item);
            }
        }

        var cancelled = false;
        Exception? persistError = null;
        if (persistDocuments && baseForDocuments is not null)
        {
            try
            {
                await PersistDocumentChangesAsync(
                        baseForDocuments,
                        candidate,
                        saved,
                        savedTexts,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                persistError = ex;
            }
        }

        if (cancelled || persistError is not null)
        {
            return await FinishAfterSideEffectsAsync(
                    workspace,
                    saved,
                    savedTexts,
                    unappliedProject: true,
                    cancelled ? WorkspaceWriteStatus.Cancelled : WorkspaceWriteStatus.PartialPersistence,
                    cancelled ? "cancelled" : persistError!.GetType().Name + ": " + persistError.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (TryApplyWorkspaceChanges(workspace, cleaned))
        {
            SetPublishedSolution(workspace.CurrentSolution);
            LogProcessWorkingSet("document_update");
            return RememberWrite(new WorkspaceWriteResult
            {
                Status = WorkspaceWriteStatus.FullSuccess,
                SavedPaths = saved,
                WorkspaceApplied = true,
                OverlayPublished = true,
            });
        }

        _logger.LogWarning(
            "TryApplyChanges rejected after preflight. saved={SavedCount}",
            saved.Count);
        return await FinishAfterSideEffectsAsync(
                workspace,
                saved,
                savedTexts,
                unappliedProject: true,
                WorkspaceWriteStatus.PartialPersistence,
                "try-apply-rejected",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PersistDocumentChangesAsync(
        Solution oldSolution,
        Solution newSolution,
        List<string> saved,
        List<(string Path, string Text)> savedTexts,
        CancellationToken cancellationToken)
    {
        foreach (var project in newSolution.Projects)
        {
            foreach (var newDoc in project.Documents)
            {
                if (newDoc.FilePath is null)
                {
                    continue;
                }

                var oldDoc = oldSolution.GetDocument(newDoc.Id);
                var newSourceText = await newDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var text = newSourceText.ToString();
                SourceText? oldSourceText = null;
                if (oldDoc is not null)
                {
                    oldSourceText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    if (string.Equals(oldSourceText.ToString(), text, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var fullPath = Path.GetFullPath(newDoc.FilePath);
                if (IsMissingOnDisk(fullPath))
                {
                    _logger.LogWarning(
                        "Skip persist of missing-on-disk path {Path} until workspace reload.",
                        fullPath);
                    continue;
                }

                if (oldDoc is null)
                {
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }

                if (FailNextDocumentWritePath is not null
                    && (FailNextDocumentWritePath == "*"
                        || string.Equals(Path.GetFullPath(FailNextDocumentWritePath), fullPath, _pathComparison)))
                {
                    FailNextDocumentWritePath = null;
                    throw new IOException("injected-file-write-failure:" + fullPath);
                }

                // One operation writes the file twice (persist here, again on TryApplyChanges); both must use the
                // same encoding, or a BOM added by one is reverted by the other.
                var writeEncoding = SourceTextEncoding.ResolveForWrite(oldSourceText, newSourceText, fullPath);
                SuppressDiskWatchForPath(fullPath);
                await File.WriteAllTextAsync(fullPath, text, writeEncoding, cancellationToken).ConfigureAwait(false);
                saved.Add(fullPath);
                savedTexts.Add((fullPath, text));
                if (CancelAfterDocumentWrites > 0 && saved.Count >= CancelAfterDocumentWrites)
                {
                    CancelAfterDocumentWrites = 0;
                    throw new OperationCanceledException("CancelAfterDocumentWrites");
                }
            }
        }
    }

    private async Task<WorkspaceWriteResult> FinishAfterSideEffectsAsync(
        Workspace workspace,
        List<string> saved,
        List<(string Path, string Text)> savedTexts,
        bool unappliedProject,
        WorkspaceWriteStatus persistenceStatus,
        string reason,
        CancellationToken cancellationToken)
    {
        if (savedTexts.Count == 0)
        {
            return RememberWrite(new WorkspaceWriteResult
            {
                Status = persistenceStatus,
                Reason = reason,
                SavedPaths = saved,
                UnappliedProjectState = unappliedProject,
            });
        }

        var reconciled = await ReconcileSavedTextsUnderLockAsync(workspace, savedTexts, cancellationToken)
            .ConfigureAwait(false);
        if (reconciled)
        {
            SetPublishedSolution(workspace.CurrentSolution);
            return RememberWrite(new WorkspaceWriteResult
            {
                Status = WorkspaceWriteStatus.ReconciliationSucceeded,
                Reason = reason,
                SavedPaths = saved,
                WorkspaceApplied = false,
                OverlayPublished = true,
                UnappliedProjectState = unappliedProject,
            });
        }

        return RememberWrite(new WorkspaceWriteResult
        {
            Status = WorkspaceWriteStatus.ReconciliationFailed,
            Reason = reason + "; reconciliation-failed",
            SavedPaths = saved,
            OverlayPublished = false,
            UnappliedProjectState = true,
        });
    }

    private async Task<bool> ReconcileSavedTextsUnderLockAsync(
        Workspace workspace,
        IReadOnlyList<(string Path, string Text)> savedTexts,
        CancellationToken cancellationToken)
    {
        if (FailNextReconciliation)
        {
            FailNextReconciliation = false;
            return false;
        }

        var current = workspace.CurrentSolution;
        var changed = false;
        foreach (var (path, text) in savedTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            var documentId = FindDocumentIdForPath(current, fullPath, _pathComparison);
            if (documentId is null)
            {
                return false;
            }

            var existingDocument = current.GetDocument(documentId);
            var existingText = existingDocument is null
                ? null
                : await existingDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var writeEncoding = SourceTextEncoding.ResolveForWrite(existingText, candidate: null, fullPath);
            current = current.WithDocumentText(documentId, SourceText.From(text, writeEncoding));
            changed = true;
        }

        if (!changed)
        {
            return true;
        }

        if (!TryApplyWorkspaceChanges(workspace, current))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads or returns cached solution. Caller must hold <see cref="_workspaceLock"/>.
    /// Compares only <paramref name="passedConfiguration"/> / platform / TFM that were actually
    /// supplied; loads with the effective (merged) property set.
    /// </summary>
    private async Task<Solution> LoadCoreAsync(
        string fullPath,
        string? passedConfiguration,
        string? passedPlatform,
        string? passedTargetFramework,
        string? effectiveConfiguration,
        string? effectivePlatform,
        string? effectivePlatformRaw,
        string? effectiveTargetFramework,
        string? effectiveBuildArgs,
        bool applyBuildArgs,
        bool publishLoadedSolution,
        CancellationToken cancellationToken)
    {
        if (_workspace is not null
            && MsBuildWorkspaceProperties.MatchesPassedLoadArguments(
                _loadedPath,
                LoadedConfiguration,
                LoadedPlatform,
                LoadedTargetFramework,
                fullPath,
                passedConfiguration,
                passedPlatform,
                passedTargetFramework,
                _pathComparison)
            && !_projectGraphStale)
        {
            if (applyBuildArgs)
            {
                ApplySessionBuildArgs(effectiveBuildArgs);
            }

            // Same canonical Platform for the workspace; refresh raw spelling only when platform was passed.
            if (passedPlatform is not null)
            {
                LoadedPlatformRaw = effectivePlatformRaw;
            }

            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
            LastLoadWasCacheHit = true;
            LastLoadReopenedGraph = false;
            LastPrepareAttempted = false;
            var cached = _solution ?? _workspace.CurrentSolution;
            LogProcessWorkingSet("workspace_load_cached");
            return cached;
        }

        StopDiskWatcherUnderLock();
        _workspace?.Dispose();
        _workspace = null;
        _solution = null;
        _loadedPath = null;
        LoadedConfiguration = null;
        LoadedPlatform = null;
        LoadedPlatformRaw = null;
        LoadedTargetFramework = null;
        LoadedBuildArgs = null;
        _dirtySourcePaths.Clear();
        _selfWriteUntilTicks.Clear();
        _missingOnDiskPaths.Clear();
        _refreshAllDocuments = false;
        ClearProjectGraphStale();
        ShadowCopyAnalyzersEnabled = false;
        ShadowCopyRootDirectory = null;
        AnalyzerShadowMapping = null;
        AnalyzerProvenanceSnapshot = null;
        _lastPublishedWriteContext = null;
        LastWriteResult = null;
        LoadSessionId = Guid.NewGuid();
        LastRefreshStale = false;
        LastExecutionObservation = AnalyzerExecutionObservation.None;
        LastPublicationPlan = null;
        _publicationState = SemanticPublicationState.None;
        LastLoadWasCacheHit = false;
        LastLoadReopenedGraph = true;
        LastPrepareAttempted = false;
        LastPrepareInjectedFailure = false;
        _ = typeof(CSharpFormattingOptions).Assembly.FullName;
        var properties = MsBuildWorkspaceProperties.Create(
            effectiveConfiguration,
            effectivePlatform,
            effectiveTargetFramework);
        var workspace = properties.Count == 0
            ? MSBuildWorkspace.Create(MsBuildHostServices)
            : MSBuildWorkspace.Create(properties, MsBuildHostServices);
        var capturedDiagnostics = new List<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            capturedDiagnostics.Add(e.Diagnostic);
            _logger.LogWarning(
                "MSBuildWorkspace {Kind}: {Message}",
                e.Diagnostic.Kind,
                e.Diagnostic.Message);
        });

        AnalyzerProvenanceSnapshot provenanceSnapshot;
        try
        {
            AnalyzerProvenanceCaptureCount++;
            provenanceSnapshot = await _analyzerProvenanceCaptureService.OpenAndCaptureAsync(
                    workspace,
                    fullPath,
                    LoadSessionId,
                    properties,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (WorkspaceLoadGuidance.IsRoslynMsBuildBuildHostFailure(ex))
        {
            workspace.Dispose();
            throw new RoslynMsBuildBuildHostException(
                WorkspaceLoadGuidance.FormatRoslynMsBuildBuildHostFailureMessage(fullPath),
                ex);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        _workspace = workspace;
        NoteRawWorkspaceRevision();
        _loadedPath = fullPath;
        AnalyzerProvenanceSnapshot = provenanceSnapshot;
        LastExecutionObservation = AnalyzerExecutionGate.EvaluateInSolutionAnalyzers(
            workspace.CurrentSolution,
            provenanceSnapshot,
            LoadSessionId,
            AnalyzerAssemblyLoader);
        if (publishLoadedSolution)
        {
            _publicationState = SemanticPublicationState.NoOverlay;
            SetPublishedSolution(workspace.CurrentSolution);
        }
        LoadedConfiguration = effectiveConfiguration;
        LoadedPlatform = effectivePlatform;
        LoadedPlatformRaw = effectivePlatformRaw;
        LoadedTargetFramework = effectiveTargetFramework;
        if (applyBuildArgs)
        {
            ApplySessionBuildArgs(effectiveBuildArgs);
        }
        else
        {
            LoadedBuildArgs = effectiveBuildArgs;
        }

        LastDiagnostics = CollectDiagnostics(workspace, capturedDiagnostics);
        StartDiskWatcherUnderLock(
            fullPath,
            workspace.CurrentSolution.Projects.Select(static project => project.FilePath));
        _logger.LogInformation(
            "Loaded Roslyn workspace from {Path} (Configuration={Configuration}, Platform={Platform}, PlatformRaw={PlatformRaw}, TargetFramework={TargetFramework}, BuildArgs={BuildArgs})",
            fullPath,
            effectiveConfiguration ?? "(default)",
            effectivePlatform ?? "(default)",
            effectivePlatformRaw ?? "(default)",
            effectiveTargetFramework ?? "(default)",
            effectiveBuildArgs ?? "(none)");
        LogProcessWorkingSet("workspace_load");
        return publishLoadedSolution ? _solution ?? workspace.CurrentSolution : workspace.CurrentSolution;
    }

    private bool HasBlockingLoadFailure(Solution solution)
    {
        if (!solution.Projects.Any())
        {
            return true;
        }

        return LastDiagnostics
            .Select(diagnostic => WorkspaceDiagnosticFormatter.Format(
                diagnostic.Kind.ToString(),
                diagnostic.Message))
            .Any(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure);
    }

    /// <summary>
    /// Must be called with <see cref="_workspaceLock"/> held.
    /// </summary>
    private async Task FlushDirtyDocumentsUnderLockAsync(CancellationToken cancellationToken)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            _dirtySourcePaths.Clear();
            _refreshAllDocuments = false;
            return;
        }

        var refreshAll = _refreshAllDocuments;
        if (!refreshAll && _dirtySourcePaths.IsEmpty)
        {
            return;
        }

        var started = Stopwatch.StartNew();
        var dirty = new List<string>();
        foreach (var key in _dirtySourcePaths.Keys)
        {
            if (_dirtySourcePaths.TryRemove(key, out _))
            {
                dirty.Add(key);
            }
        }

        _refreshAllDocuments = false;

        var result = await WorkspaceDocumentDiskSync.ApplyAsync(
            workspace.CurrentSolution,
            dirty,
            refreshAll,
            _pathComparison,
            cancellationToken).ConfigureAwait(false);

        if (result.Unrepresentable.Count > 0 || refreshAll)
        {
            MarkProjectGraphStaleFromComposition();
            _logger.LogWarning(
                "workspace_disk_sync composition unknown unrepresentable={Count} refreshAll={RefreshAll} paths={Paths}",
                result.Unrepresentable.Count,
                refreshAll,
                result.Unrepresentable.Count == 0
                    ? "(none)"
                    : string.Join("; ", result.Unrepresentable));
        }

        var inputSolution = workspace.CurrentSolution;
        foreach (var path in result.Unrepresentable)
        {
            if (File.Exists(path))
            {
                continue;
            }

            if (FindDocumentIdForPath(inputSolution, path, _pathComparison) is not null)
            {
                _missingOnDiskPaths.TryAdd(path, 0);
            }
        }

        if (result.Updated == 0
            && ReferenceEquals(result.Solution, workspace.CurrentSolution))
        {
            if (dirty.Count > 0 || refreshAll)
            {
                NoteRawWorkspaceRevision();
            }

            if (dirty.Count > 0 || refreshAll || result.Unrepresentable.Count > 0)
            {
                _logger.LogInformation(
                    "workspace_disk_sync updated={Updated} added={Added} removed={Removed} unchanged={Unchanged} unrepresentable={Unrepresentable} refreshAll={RefreshAll} elapsedMs={ElapsedMs} write=\"None\"",
                    result.Updated,
                    result.Added,
                    result.Removed,
                    result.Unchanged,
                    result.Unrepresentable.Count,
                    refreshAll,
                    started.ElapsedMilliseconds);
            }

            return;
        }

        var alreadyOnDisk = new List<(string Path, string Text)>();
        foreach (var path in dirty)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (FindDocumentIdForPath(inputSolution, path, _pathComparison) is null)
            {
                continue;
            }

            alreadyOnDisk.Add((path, await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)));
        }

        var rawBase = workspace.CurrentSolution;
        var context = CreateVerifiedWriteContext(_solution, rawBase);
        var write = await ApplyWorkspaceWriteUnderLockAsync(
                result.Solution,
                rawBase,
                context,
                persistDocuments: false,
                alreadyOnDisk,
                cancellationToken)
            .ConfigureAwait(false);
        if (!write.IsFullSuccess && write.Status != WorkspaceWriteStatus.ReconciliationSucceeded)
        {
            _logger.LogWarning(
                "workspace_disk_sync write {Status} reason={Reason} updated={Updated} added={Added} removed={Removed} unrepresentable={Unrepresentable}.",
                write.Status,
                write.Reason,
                result.Updated,
                result.Added,
                result.Removed,
                result.Unrepresentable.Count);
            return;
        }

        _logger.LogInformation(
            "workspace_disk_sync updated={Updated} added={Added} removed={Removed} unchanged={Unchanged} unrepresentable={Unrepresentable} refreshAll={RefreshAll} elapsedMs={ElapsedMs} write={WriteStatus}",
            result.Updated,
            result.Added,
            result.Removed,
            result.Unchanged,
            result.Unrepresentable.Count,
            refreshAll,
            started.ElapsedMilliseconds,
            write.Status);
        LogProcessWorkingSet("document_update");
    }

    private static void TryAddWatchDirectory(HashSet<string> directories, string? filePath)
    {
        var directory = TryGetFileDirectory(filePath);
        if (directory is not null)
        {
            directories.Add(directory);
        }
    }

    private static string? TryGetFileDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath.Trim());
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            return Path.GetFullPath(directory);
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is strictly inside <paramref name="parent"/>
    /// (trailing separators ignored; <c>repo</c> does not contain <c>repo-other</c>).
    /// </summary>
    private static bool IsStrictSubdirectory(string candidate, string parent, StringComparison comparison)
    {
        var parentTrim = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidateTrim = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(parentTrim, candidateTrim, comparison))
        {
            return false;
        }

        var prefix = parentTrim + Path.DirectorySeparatorChar;
        if (candidateTrim.StartsWith(prefix, comparison))
        {
            return true;
        }

        if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar)
        {
            var altPrefix = parentTrim + Path.AltDirectorySeparatorChar;
            if (candidateTrim.StartsWith(altPrefix, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private void StartDiskWatcherUnderLock(string workspaceFilePath, IEnumerable<string?> projectFilePaths)
    {
        StopDiskWatcherUnderLock();
        // Capture once before enabling events. Deleted and renamed loaded projects still belong
        // to this graph even when their backing files no longer exist. See docs/wpf-temporary-project-watching/README.md.
        var session = new WorkspaceInputSession(projectFilePaths);
        _diskWatcherSession = session;
        var roots = ComputeWatchRoots(workspaceFilePath, session.LoadedProjectPaths);
        foreach (var directory in roots)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                _logger.LogDebug("Disk watcher not started: workspace directory missing ({Path}).", directory);
                continue;
            }

            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
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

                watcher.Changed += (_, args) => OnDiskWatcherChanged(session, args);
                watcher.Created += (_, args) => OnDiskWatcherChanged(session, args);
                watcher.Deleted += (_, args) => OnDiskWatcherChanged(session, args);
                watcher.Renamed += (_, args) => OnDiskWatcherRenamed(session, args);
                watcher.Error += (_, args) => OnDiskWatcherError(session, args);
                watcher.EnableRaisingEvents = true;
                _diskWatchers.Add(watcher);
                watcher = null;
                _logger.LogInformation("Disk watcher started on {Directory}", directory);
            }
            catch (Exception ex)
            {
                watcher?.Dispose();
                _logger.LogWarning(ex, "Disk watcher failed to start for {Directory}. Symbol search stays on the load snapshot until reset_workspace.", directory);
            }
        }
    }

    private void StopDiskWatcherUnderLock()
    {
        // Closure waits for admitted mutations before reset clears flags or installs another session.
        // A token check alone would let a callback write new-session state after passing the check.
        _diskWatcherSession?.Close();
        _diskWatcherSession = null;
        if (_diskWatchers.Count == 0)
        {
            return;
        }

        var watchers = _diskWatchers.ToArray();
        _diskWatchers.Clear();
        foreach (var watcher in watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
            }
            catch (ObjectDisposedException)
            {
            }

            watcher.Dispose();
        }
    }

    private void OnDiskWatcherChanged(WorkspaceInputSession session, FileSystemEventArgs e)
    {
        BeforeDiskWatcherCallbackForTests?.Invoke();
        string? graphPath = null;
        session.TryRun(() => graphPath = QueueDiskPath(session, e.FullPath));
        LogChangedGraphPath(graphPath);
        AfterDiskWatcherChangeForTests?.Invoke(e);
    }

    private void OnDiskWatcherRenamed(WorkspaceInputSession session, RenamedEventArgs e)
    {
        BeforeDiskWatcherCallbackForTests?.Invoke();
        var directoryRenamed = false;
        string? oldGraphPath = null;
        string? newGraphPath = null;
        var accepted = session.TryRun(() =>
        {
            if (Directory.Exists(e.FullPath) || Directory.Exists(e.OldFullPath))
            {
                _refreshAllDocuments = true;
                directoryRenamed = true;
                return;
            }
            oldGraphPath = QueueDiskPath(session, e.OldFullPath);
            newGraphPath = QueueDiskPath(session, e.FullPath);
        });
        if (accepted && directoryRenamed)
        {
            LogDiskWatcherCallback(
                LogLevel.Information,
                exception: null,
                "Disk watcher: directory rename, will refresh known documents on next semantic call.");
        }
        LogChangedGraphPath(oldGraphPath);
        LogChangedGraphPath(newGraphPath);
    }

    private void OnDiskWatcherError(WorkspaceInputSession session, ErrorEventArgs e)
    {
        BeforeDiskWatcherCallbackForTests?.Invoke();
        if (!session.TryRun(() => _refreshAllDocuments = true))
        {
            return;
        }
        LogDiskWatcherCallback(
            LogLevel.Warning,
            e.GetException(),
            "Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.");
    }

    /// <summary>
    /// Watcher callbacks run on a thread-pool thread. <c>Logger.Log</c> rethrows when a provider throws,
    /// and that exception is unhandled and kills the process. Windows Event Log does this after its
    /// handle is disposed; a hosted runner often cannot write that log. The caller has already requested
    /// a full re-read, so a failed write still degrades instead of crashing.
    /// </summary>
    private void LogDiskWatcherCallback(LogLevel level, Exception? exception, string message)
    {
        try
        {
            _logger.Log(level, exception, message);
        }
        catch (Exception)
        {
            // The refresh flag is already set. Reporting this failure through the same logger would throw again.
        }
    }

    private void LogChangedGraphPath(string? graphPath)
    {
        if (graphPath is not null)
        {
            // Providers are external code: do not run them inside the session's closure gate.
            LogDiskWatcherCallback(LogLevel.Information, exception: null,
                $"Project graph file changed on disk: {graphPath}");
        }
    }

    private string? QueueDiskPath(WorkspaceInputSession session, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || WorkspaceDiskPathFilter.IsIgnoredPath(rawPath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception)
        {
            return null;
        }

        if (IsSelfWriteSuppressed(fullPath))
        {
            return null;
        }

        if (WorkspaceDiskPathFilter.IsProjectGraphFile(fullPath))
        {
            if (WorkspaceDiskPathFilter.IsWpfTemporaryProject(fullPath)
                && !session.LoadedProjectPaths.Contains(fullPath))
            {
                return null;
            }
            MarkProjectGraphStaleFromGraphFile();
            return fullPath;
        }

        if (!WorkspaceDiskPathFilter.IsCSharpSource(fullPath))
        {
            return null;
        }

        _dirtySourcePaths.TryAdd(fullPath, 0);
        return null;
    }

    private bool IsSelfWriteSuppressed(string fullPath)
    {
        if (!_selfWriteUntilTicks.TryGetValue(fullPath, out var untilTicks))
        {
            return false;
        }

        if (Environment.TickCount64 < untilTicks)
        {
            return true;
        }

        _selfWriteUntilTicks.TryRemove(fullPath, out _);
        return false;
    }

    private bool IsMissingOnDisk(string fullPath) => _missingOnDiskPaths.ContainsKey(fullPath);

    private void MarkProjectGraphStaleFromGraphFile()
    {
        _projectGraphStaleFromGraphFile = true;
        _projectGraphStale = true;
    }

    private void MarkProjectGraphStaleFromComposition()
    {
        _projectGraphStaleFromComposition = true;
        _projectGraphStale = true;
    }

    private void ClearProjectGraphStale()
    {
        _projectGraphStale = false;
        _projectGraphStaleFromGraphFile = false;
        _projectGraphStaleFromComposition = false;
    }

    private static DocumentId? FindDocumentIdForPath(
        Solution solution,
        string fullFilePath,
        StringComparison pathComparison)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var fp = document.FilePath;
                if (fp is not null
                    && string.Equals(Path.GetFullPath(fp), fullFilePath, pathComparison))
                {
                    return document.Id;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<WorkspaceDiagnostic> CollectDiagnostics(
        MSBuildWorkspace workspace,
        IReadOnlyCollection<WorkspaceDiagnostic> capturedDiagnostics)
    {
        return workspace.Diagnostics
            .Concat(capturedDiagnostics)
            .DistinctBy(d => $"{d.Kind}:{d.Message}")
            .ToArray();
    }

    /// <summary>
    /// Logs process working set via Serilog (same pipeline style as <see cref="Diagnostics.ToolTelemetry"/>).
    /// </summary>
    private static void LogProcessWorkingSet(string context)
    {
        var bytes = Process.GetCurrentProcess().WorkingSet64;
        var sizeMb = bytes / (1024.0 * 1024.0);

        if (bytes > MemoryWarningThresholdBytes)
        {
            Log.Warning(
                "[Memory Alert] Roslyn server is consuming > 1.5GB of RAM. Current: {Size:F1} MB (context: {Context})",
                sizeMb,
                context);
        }
        else
        {
            Log.Information(
                "Roslyn server working set: {Size:F1} MB (context: {Context})",
                sizeMb,
                context);
        }
    }

    private static IEnumerable<Assembly> LoadMefAssemblies()
    {
        yield return typeof(Workspace).Assembly;
        yield return typeof(CSharpFormattingOptions).Assembly;
        yield return typeof(MSBuildWorkspace).Assembly;
        yield return Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features"));
        yield return Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"));
    }

    internal readonly record struct DirtySourceWaitResult(
        bool Delivered,
        bool TimedOut,
        TimeSpan Elapsed,
        int PendingCount);

    internal const string ProjectGraphFileStaleHint =
        "> **Note:** A `.csproj` / `.sln` / `Directory.Build.props` changed on disk. Saved `.cs` files are synced; "
        + "package refs and compile globs may be stale. Call `reset_workspace` then `load_workspace` "
        + "(or `load_workspace` alone — a stale project graph skips the load cache).";

    internal const string ProjectGraphCompositionStaleHint =
        "> **Note:** A saved `.cs` file appeared or disappeared outside the loaded workspace snapshot. "
        + "MSBuild decides membership on reload — the file is not guaranteed to enter the workspace. Call `reset_workspace` then `load_workspace` "
        + "(or `load_workspace` alone — a stale project graph skips the load cache).";

    /// <summary>
    /// Working set threshold (bytes) above which a memory warning is emitted.
    /// </summary>
    private const long MemoryWarningThresholdBytes = 1500L * 1024 * 1024;

    /// <summary>Ignore FileSystemWatcher events for paths we just wrote (partial-read race).</summary>
    private const int SelfWriteSuppressMs = 1000;

    /// <summary>
    /// Explicit MEF host so MSBuildWorkspace discovers the C# language / project loader
    /// (fixes "language 'C#' is not supported" when using parameterless MSBuildWorkspace.Create()).
    /// </summary>
    private static readonly HostServices MsBuildHostServices = MefHostServices.Create(
        LoadMefAssemblies());
    private readonly ILogger<SolutionManager> _logger;
    private readonly AnalyzerProvenanceCaptureService _analyzerProvenanceCaptureService;
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly ConcurrentDictionary<string, byte> _dirtySourcePaths;
    private readonly ConcurrentDictionary<string, long> _selfWriteUntilTicks;
    private readonly List<FileSystemWatcher> _diskWatchers = new();
    private WorkspaceInputSession? _diskWatcherSession;
    private readonly ConcurrentDictionary<string, byte> _missingOnDiskPaths;
    private volatile bool _refreshAllDocuments;
    private volatile bool _projectGraphStale;
    private volatile bool _projectGraphStaleFromGraphFile;
    private volatile bool _projectGraphStaleFromComposition;
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private Solution? _sanitizedPublishedSolution;
    private Solution? _sanitizedPublishedSolutionSource;

    private SemanticPublicationState _publicationState = SemanticPublicationState.None;
    private readonly ConditionalWeakTable<Solution, WorkspaceWriteOperationContext> _operationContexts = new();
    private WorkspaceWriteOperationContext? _lastPublishedWriteContext;
    private long _rawWorkspaceRevision;
    private string? _loadedPath;
    private volatile bool _workspaceLoadInProgress;
}
