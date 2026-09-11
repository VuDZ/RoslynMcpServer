using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Serilog;

public sealed class SolutionManager
{
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

    private static IEnumerable<Assembly> LoadMefAssemblies()
    {
        yield return typeof(Workspace).Assembly;
        yield return typeof(CSharpFormattingOptions).Assembly;
        yield return typeof(MSBuildWorkspace).Assembly;
        yield return Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features"));
        yield return Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"));
    }

    private readonly ILogger<SolutionManager> _logger;
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, byte> _dirtySourcePaths;
    private readonly ConcurrentDictionary<string, long> _selfWriteUntilTicks;
    private FileSystemWatcher? _diskWatcher;
    private volatile bool _refreshAllDocuments;
    private volatile bool _projectGraphStale;

    private readonly InProcessAnalyzerAssemblyLoader _analyzerAssemblyLoader = new();

    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    // Overlay state is not one enabled bool: mapping is prepared/active generations,
    // _lastRefreshStale/_lastShadowCopyResults are the last refresh, sticky
    // _shadowCopyAnalyzersEnabled is U-ARB-05 active overlay. Observed CLR execution
    // is not stored (epoch 3). Holding mapping for an in-flight write is epoch 4.
    private bool _shadowCopyAnalyzersEnabled;
    private string? _shadowCopyRootDirectory;
    private Guid _loadSessionId;
    private AnalyzerShadowMapping? _analyzerShadowMapping;
    private bool _lastLoadWasCacheHit;
    private bool _lastLoadReopenedGraph;
    private bool _lastPrepareAttempted;
    private bool _lastPrepareInjectedFailure;
    private bool _lastRefreshStale;
    private int _overlayPrepareCount;
    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> _lastShadowCopyResults =
        Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
    private string? _loadedPath;
    private string? _loadedConfiguration;
    private string? _loadedPlatform;
    private string? _loadedTargetFramework;
    private string? _loadedBuildArgs;
    private IReadOnlyList<WorkspaceDiagnostic> _lastDiagnostics = Array.Empty<WorkspaceDiagnostic>();

    public SolutionManager(ILogger<SolutionManager> logger)
    {
        _logger = logger;
        _dirtySourcePaths = new ConcurrentDictionary<string, byte>(_pathComparer);
        _selfWriteUntilTicks = new ConcurrentDictionary<string, long>(_pathComparer);
    }

    public IReadOnlyList<WorkspaceDiagnostic> LastDiagnostics => _lastDiagnostics;

    /// <summary>True when the last <see cref="LoadAsync"/> reused the existing workspace graph (cached load).</summary>
    internal bool LastLoadWasCacheHit => _lastLoadWasCacheHit;

    /// <summary>True when the last <see cref="LoadAsync"/> disposed and reopened the MSBuild graph.</summary>
    internal bool LastLoadReopenedGraph => _lastLoadReopenedGraph;

    /// <summary>True when the last overlay prepare/refresh attempted analyzer file I/O.</summary>
    internal bool LastPrepareAttempted => _lastPrepareAttempted;

    /// <summary>True when the last overlay prepare used the injected prepare-failure seam.</summary>
    internal bool LastPrepareInjectedFailure => _lastPrepareInjectedFailure;

    /// <summary>True when the last refresh failed and a previous compatible mapping was kept as stale.</summary>
    internal bool LastRefreshStale => _lastRefreshStale;

    internal int OverlayPrepareCount => _overlayPrepareCount;

    internal IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> LastShadowCopyResults => _lastShadowCopyResults;

    internal bool ShadowCopyAnalyzersEnabled => _shadowCopyAnalyzersEnabled;

    internal string? ShadowCopyRootDirectory => _shadowCopyRootDirectory;

    internal AnalyzerShadowMapping? AnalyzerShadowMapping => _analyzerShadowMapping;

    internal Guid LoadSessionId => _loadSessionId;

    /// <summary>
    /// When true, the next <see cref="ShadowCopyInSolutionAnalyzerReferencesAsync"/> skips file preparation
    /// (simulates a failed refresh). Document edit / flush / post-apply reapply the existing mapping and do
    /// not consume this seam. Test/host only; production never sets this.
    /// </summary>
    internal bool FailNextOverlayPrepare { get; set; }

    internal IReadOnlyList<string> GetPendingDirtySourcePaths() => _dirtySourcePaths.Keys.ToArray();

    internal Solution? GetWorkspaceCurrentSolution() => _workspace?.CurrentSolution;

    internal InProcessAnalyzerAssemblyLoader AnalyzerAssemblyLoader => _analyzerAssemblyLoader;

    /// <summary>MSBuild <c>Configuration</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedConfiguration => _loadedConfiguration;

    /// <summary>MSBuild <c>Platform</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedPlatform => _loadedPlatform;

    /// <summary>MSBuild <c>TargetFramework</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedTargetFramework => _loadedTargetFramework;

    /// <summary>
    /// Extra <c>dotnet build</c> args from the last <see cref="LoadAsync"/> (session CLI only; not an MSBuildWorkspace property).
    /// </summary>
    public string? LoadedBuildArgs => _loadedBuildArgs;

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
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            throw new ArgumentException("Solution or project path cannot be empty.", nameof(solutionOrProjectPath));
        }

        var fullPath = Path.GetFullPath(solutionOrProjectPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Solution or project file not found.", fullPath);
        }

        var normalizedConfiguration = DotNetConfigurationArguments.Normalize(configuration, nameof(configuration));
        var normalizedPlatform = DotNetConfigurationArguments.NormalizePlatform(platform);
        var normalizedTargetFramework = DotNetConfigurationArguments.Normalize(targetFramework, nameof(targetFramework));
        var normalizedBuildArgs = DotNetBuildArguments.Normalize(buildArgs);

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(
                fullPath,
                normalizedConfiguration,
                normalizedPlatform,
                normalizedTargetFramework,
                normalizedBuildArgs,
                cancellationToken);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Updates Roslyn's in-memory document text to match disk content (must be called under the same
    /// path normalization as the workspace). Uses <see cref="Workspace.TryApplyChanges"/> — the supported
    /// public API equivalent of applying <see cref="Solution.WithDocumentText"/>.
    /// </summary>
    public async Task UpdateDocumentInMemoryAsync(
        string filePath,
        string newText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            var workspace = _workspace;
            if (workspace is null)
            {
                _logger.LogDebug("Skip in-memory document update: no workspace loaded ({Path}).", fullPath);
                return;
            }

            SuppressDiskWatchForPath(fullPath);

            var documentId = FindDocumentIdForPath(workspace.CurrentSolution, fullPath, _pathComparison);
            if (documentId is null)
            {
                _logger.LogDebug("Skip in-memory document update: file not part of loaded workspace ({Path}).", fullPath);
                return;
            }

            var newSolution = workspace.CurrentSolution.WithDocumentText(
                documentId,
                SourceText.From(newText ?? string.Empty, Encoding.UTF8));

            if (!workspace.TryApplyChanges(newSolution))
            {
                _logger.LogWarning("TryApplyChanges failed for in-memory update of {Path}.", fullPath);
                return;
            }

            _solution = ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution);
            LogProcessWorkingSet("document_update");
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
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureWorkspaceLoadedForFileUnderLockAsync(fullFilePath, cancellationToken);
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken);

            var workspace = _workspace;
            if (workspace is null)
            {
                return null;
            }

            // Route through the analyzer-shadow-copy overlay (GetCurrentSolution), not workspace.CurrentSolution
            // directly — otherwise document.GetSemanticModelAsync() on the returned Document would still compile
            // against the original (possibly broken/locked) AnalyzerReference. See
            // ShadowCopyInSolutionAnalyzerReferencesAsync for why the overlay is never pushed into the workspace.
            var solution = GetCurrentSolution() ?? workspace.CurrentSolution;
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
        return _solution ?? _workspace?.CurrentSolution;
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
            var workspace = _workspace;
            var loadedPath = _loadedPath;
            if (workspace is null || loadedPath is null)
            {
                return Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
            }

            var shadowRoot = AnalyzerReferenceShadowCopier.GetDefaultShadowRootDirectory(loadedPath);
            _lastPrepareAttempted = true;
            _overlayPrepareCount++;

            if (FailNextOverlayPrepare)
            {
                FailNextOverlayPrepare = false;
                _lastPrepareInjectedFailure = true;
                return CompleteFailedPrepare(
                    workspace.CurrentSolution,
                    shadowRoot,
                    "injected-prepare-failure");
            }

            _lastPrepareInjectedFailure = false;
            var prepared = AnalyzerReferenceShadowCopier.PrepareInSolutionAnalyzerReferences(
                workspace.CurrentSolution,
                shadowRoot,
                _analyzerAssemblyLoader,
                _analyzerShadowMapping,
                _loadSessionId,
                loadedPath);
            return CompletePrepare(workspace.CurrentSolution, shadowRoot, prepared);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Reapplies the stored analyzer-reference mapping on top of <paramref name="solution"/> with no analyzer
    /// file I/O. Every caller that would otherwise cache <c>workspace.CurrentSolution</c> into <c>_solution</c>
    /// must route through here so the overlay survives document edit / disk sync. A no-op when this load
    /// session has no mapping.
    /// </summary>
    private Solution ApplyShadowCopyOverlayIfEnabled(Solution solution)
    {
        var mapping = _analyzerShadowMapping;
        if (!_shadowCopyAnalyzersEnabled
            || mapping is null
            || mapping.SessionId != _loadSessionId)
        {
            return solution;
        }

        if (!string.IsNullOrWhiteSpace(mapping.LoadedPath)
            && !string.IsNullOrWhiteSpace(_loadedPath)
            && !string.Equals(mapping.LoadedPath, _loadedPath, _pathComparison))
        {
            return solution;
        }

        return mapping.Apply(solution, _analyzerAssemblyLoader);
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> CompletePrepare(
        Solution workspaceSolution,
        string shadowRoot,
        AnalyzerShadowPrepareOutcome prepared)
    {
        _lastShadowCopyResults = prepared.Results;
        _lastRefreshStale = prepared.UsedPreviousMappingAsStale
            || prepared.Mapping.Entries.Any(e => e.StaleGeneration);

        if (prepared.Mapping.HasAnyApplied)
        {
            _analyzerShadowMapping = prepared.Mapping;
            _shadowCopyAnalyzersEnabled = true;
            _shadowCopyRootDirectory = shadowRoot;
            _solution = prepared.Mapping.Apply(workspaceSolution, _analyzerAssemblyLoader);
        }

        return prepared.Results;
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> CompleteFailedPrepare(
        Solution workspaceSolution,
        string shadowRoot,
        string reason)
    {
        if (_analyzerShadowMapping is { HasAnyApplied: true } previous)
        {
            var stale = previous.WithStale(reason);
            _analyzerShadowMapping = stale;
            _lastShadowCopyResults = AnalyzerReferenceShadowCopier.ToRewriteResults(stale);
            _lastRefreshStale = true;
            _shadowCopyAnalyzersEnabled = true;
            _shadowCopyRootDirectory = shadowRoot;
            _solution = stale.Apply(workspaceSolution, _analyzerAssemblyLoader);
            return _lastShadowCopyResults;
        }

        _lastShadowCopyResults = Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
        _lastRefreshStale = false;
        return _lastShadowCopyResults;
    }

    /// <summary>
    /// Defensive guard for every remaining <see cref="Workspace.TryApplyChanges(Solution)"/> call site: strips
    /// any <see cref="AnalyzerReference"/> difference between <paramref name="candidate"/> and
    /// <paramref name="workspaceCurrentSolution"/> before the diff reaches the workspace. Without this, a tool
    /// that built its edit on top of the analyzer-shadow-copy overlay (via <see cref="GetCurrentSolution"/>) —
    /// e.g. a rename or code fix — would smuggle the overlay's <see cref="AnalyzerReference"/> change back into
    /// <c>TryApplyChanges</c>, which is exactly the disk-corrupting behavior
    /// <see cref="ShadowCopyInSolutionAnalyzerReferencesAsync"/> exists to avoid. No-op when the flag was never
    /// enabled for this load, or when a project has no analyzer-reference difference.
    /// </summary>
    private Solution RevertAnalyzerReferenceOverlayForApply(Solution candidate, Solution workspaceCurrentSolution)
    {
        if (!_shadowCopyAnalyzersEnabled)
        {
            return candidate;
        }

        foreach (var projectId in candidate.ProjectIds.ToList())
        {
            var project = candidate.GetProject(projectId);
            var originalProject = workspaceCurrentSolution.GetProject(projectId);
            if (project is null || originalProject is null)
            {
                continue;
            }

            if (!project.AnalyzerReferences.SequenceEqual(originalProject.AnalyzerReferences))
            {
                candidate = candidate.WithProjectAnalyzerReferences(projectId, originalProject.AnalyzerReferences);
            }
        }

        return candidate;
    }

    /// <summary>
    /// Applies queued on-disk <c>.cs</c> changes (FileSystemWatcher dirty set) then returns the snapshot.
    /// Unsaved editor buffers are ignored — only files already written to disk.
    /// </summary>
    public async Task<Solution?> GetCurrentSolutionAfterDiskSyncAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDiskChangesAppliedAsync(cancellationToken).ConfigureAwait(false);
        return GetCurrentSolution();
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

    internal readonly record struct DirtySourceWaitResult(
        bool Delivered,
        bool TimedOut,
        TimeSpan Elapsed,
        int PendingCount);

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
    /// Hint when <c>.csproj</c> / solution / Directory.Build.* changed on disk. Source <c>.cs</c> is still synced;
    /// project graph (refs, globs) needs <c>reset_workspace</c> + <c>load_workspace</c> unless the next load skips cache.
    /// </summary>
    public string? GetProjectGraphStaleHint()
    {
        if (!_projectGraphStale)
        {
            return null;
        }

        return "> **Note:** A `.csproj` / `.sln` / `Directory.Build.props` changed on disk. Saved `.cs` files are synced; "
            + "package refs and compile globs may be stale. Call `reset_workspace` then `load_workspace` "
            + "(or `load_workspace` alone — a stale project graph skips the load cache).";
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
    /// Persists solution document changes to disk and updates the in-memory workspace.
    /// Caller must not hold <see cref="_workspaceLock"/> (this method acquires it).
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplySolutionChangesToDiskAsync(
        Solution oldSolution,
        Solution newSolution,
        CancellationToken cancellationToken = default)
    {
        var changedPaths = new List<string>();
        foreach (var project in newSolution.Projects)
        {
            foreach (var newDoc in project.Documents)
            {
                if (newDoc.FilePath is null)
                {
                    continue;
                }

                var oldDoc = oldSolution.GetDocument(newDoc.Id);
                var newText = await newDoc.GetTextAsync(cancellationToken);
                var text = newText.ToString();

                if (oldDoc is null)
                {
                    var directory = Path.GetDirectoryName(newDoc.FilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    SuppressDiskWatchForPath(newDoc.FilePath);
                    await File.WriteAllTextAsync(newDoc.FilePath, text, cancellationToken);
                    changedPaths.Add(newDoc.FilePath);
                    continue;
                }

                var oldText = await oldDoc.GetTextAsync(cancellationToken);
                if (string.Equals(oldText.ToString(), text, StringComparison.Ordinal))
                {
                    continue;
                }

                SuppressDiskWatchForPath(newDoc.FilePath);
                await File.WriteAllTextAsync(newDoc.FilePath, text, cancellationToken);
                changedPaths.Add(newDoc.FilePath);
            }
        }

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            var workspace = _workspace;
            var solutionToApply = workspace is null
                ? newSolution
                : RevertAnalyzerReferenceOverlayForApply(newSolution, workspace.CurrentSolution);
            if (workspace is not null && workspace.TryApplyChanges(solutionToApply))
            {
                _solution = ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution);
            }
            else
            {
                foreach (var path in changedPaths)
                {
                    var doc = newSolution.Projects
                        .SelectMany(p => p.Documents)
                        .FirstOrDefault(d => string.Equals(Path.GetFullPath(d.FilePath ?? string.Empty), Path.GetFullPath(path), _pathComparison));
                    if (doc is not null)
                    {
                        var text = (await doc.GetTextAsync(cancellationToken)).ToString();
                        await UpdateDocumentInMemoryUnderLockAsync(path, text);
                    }
                }
            }
        }
        finally
        {
            _workspaceLock.Release();
        }

        return changedPaths;
    }

    /// <summary>
    /// Must be called with <see cref="_workspaceLock"/> held.
    /// </summary>
    private async Task UpdateDocumentInMemoryUnderLockAsync(string filePath, string newText)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        var documentId = FindDocumentIdForPath(workspace.CurrentSolution, fullPath, _pathComparison);
        if (documentId is null)
        {
            return;
        }

        var newSolution = workspace.CurrentSolution.WithDocumentText(
            documentId,
            SourceText.From(newText, Encoding.UTF8));

        if (workspace.TryApplyChanges(newSolution))
        {
            _solution = ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution);
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
            _refreshAllDocuments = false;
            _projectGraphStale = false;
            _workspace?.Dispose();
            _workspace = null;
            _solution = null;
            _shadowCopyAnalyzersEnabled = false;
            _shadowCopyRootDirectory = null;
            _analyzerShadowMapping = null;
            _loadSessionId = Guid.Empty;
            _lastRefreshStale = false;
            _loadedPath = null;
            _loadedConfiguration = null;
            _loadedPlatform = null;
            _loadedTargetFramework = null;
            _loadedBuildArgs = null;
            _lastDiagnostics = Array.Empty<WorkspaceDiagnostic>();
            _logger.LogInformation("Roslyn workspace cleared (MSBuildWorkspace disposed).");
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    /// <summary>
    /// Must be called with <see cref="_workspaceLock"/> held.
    /// </summary>
    private async Task EnsureWorkspaceLoadedForFileUnderLockAsync(
        string fullFilePath,
        CancellationToken cancellationToken)
    {
        if (_workspace is not null)
        {
            return;
        }

        var candidate = FindClosestSolutionOrProject(fullFilePath);
        if (candidate is null)
        {
            throw new FileNotFoundException(
                "Could not locate a .sln, .slnx, or .csproj while walking parent directories.",
                fullFilePath);
        }

        var candidateFull = Path.GetFullPath(candidate);
        if (!File.Exists(candidateFull))
        {
            throw new FileNotFoundException("Solution or project file not found.", candidateFull);
        }

        _ = await LoadCoreAsync(
            candidateFull,
            configuration: null,
            platform: null,
            targetFramework: null,
            buildArgs: null,
            cancellationToken);
    }

    /// <summary>
    /// Loads or returns cached solution. Caller must hold <see cref="_workspaceLock"/>.
    /// </summary>
    private async Task<Solution> LoadCoreAsync(
        string fullPath,
        string? configuration,
        string? platform,
        string? targetFramework,
        string? buildArgs,
        CancellationToken cancellationToken)
    {
        if (_workspace is not null
            && MsBuildWorkspaceProperties.IsSameLoadCache(
                _loadedPath,
                _loadedConfiguration,
                _loadedPlatform,
                _loadedTargetFramework,
                fullPath,
                configuration,
                platform,
                targetFramework,
                _pathComparison)
            && !_projectGraphStale)
        {
            ApplySessionBuildArgs(buildArgs);
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
            _lastLoadWasCacheHit = true;
            _lastLoadReopenedGraph = false;
            _lastPrepareAttempted = false;
            var cached = _solution ?? _workspace.CurrentSolution;
            LogProcessWorkingSet("workspace_load_cached");
            return cached;
        }

        StopDiskWatcherUnderLock();
        _workspace?.Dispose();
        _dirtySourcePaths.Clear();
        _selfWriteUntilTicks.Clear();
        _refreshAllDocuments = false;
        _projectGraphStale = false;
        _shadowCopyAnalyzersEnabled = false;
        _shadowCopyRootDirectory = null;
        _analyzerShadowMapping = null;
        _loadSessionId = Guid.NewGuid();
        _lastRefreshStale = false;
        _lastLoadWasCacheHit = false;
        _lastLoadReopenedGraph = true;
        _lastPrepareAttempted = false;
        _lastPrepareInjectedFailure = false;
        _ = typeof(CSharpFormattingOptions).Assembly.FullName;
        var properties = MsBuildWorkspaceProperties.Create(configuration, platform, targetFramework);
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

        try
        {
            var extension = Path.GetExtension(fullPath);
            if (string.Equals(extension, ".sln", _pathComparison)
                || string.Equals(extension, ".slnx", _pathComparison))
            {
                _ = await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken);
            }
            else if (string.Equals(extension, ".csproj", _pathComparison))
            {
                var project = await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken);
                _ = workspace.CurrentSolution.GetProject(project.Id)
                    ?? throw new InvalidOperationException($"Unable to load project '{fullPath}'.");
            }
            else
            {
                workspace.Dispose();
                throw new NotSupportedException("Only .sln, .slnx, and .csproj files are supported.");
            }
        }
        catch (Exception ex) when (WorkspaceLoadGuidance.IsRoslynMsBuildBuildHostFailure(ex))
        {
            workspace.Dispose();
            throw new RoslynMsBuildBuildHostException(
                WorkspaceLoadGuidance.FormatRoslynMsBuildBuildHostFailureMessage(fullPath),
                ex);
        }

        _workspace = workspace;
        _solution = ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution);
        _loadedPath = fullPath;
        _loadedConfiguration = configuration;
        _loadedPlatform = platform;
        _loadedTargetFramework = targetFramework;
        ApplySessionBuildArgs(buildArgs);
        _lastDiagnostics = CollectDiagnostics(workspace, capturedDiagnostics);
        StartDiskWatcherUnderLock(fullPath);
        _logger.LogInformation(
            "Loaded Roslyn workspace from {Path} (Configuration={Configuration}, Platform={Platform}, TargetFramework={TargetFramework}, BuildArgs={BuildArgs})",
            fullPath,
            configuration ?? "(default)",
            platform ?? "(default)",
            targetFramework ?? "(default)",
            buildArgs ?? "(none)");
        LogProcessWorkingSet("workspace_load");
        return _solution;
    }

    /// <summary>
    /// Session CLI extras are not part of the MSBuildWorkspace cache key.
    /// Always replace so a second <c>load_workspace</c> can change or clear them without reopening the solution.
    /// </summary>
    internal void ApplySessionBuildArgs(string? buildArgs) => _loadedBuildArgs = buildArgs;

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

        if (result.Updated == 0 && result.Added == 0 && result.Removed == 0)
        {
            return;
        }

        if (!workspace.TryApplyChanges(result.Solution))
        {
            _logger.LogWarning(
                "TryApplyChanges failed after workspace_disk_sync updated={Updated} added={Added} removed={Removed}.",
                result.Updated,
                result.Added,
                result.Removed);
            return;
        }

        _solution = ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution);
        _logger.LogInformation(
            "workspace_disk_sync updated={Updated} added={Added} removed={Removed} unchanged={Unchanged} refreshAll={RefreshAll} elapsedMs={ElapsedMs}",
            result.Updated,
            result.Added,
            result.Removed,
            result.Unchanged,
            refreshAll,
            started.ElapsedMilliseconds);
        LogProcessWorkingSet("document_update");
    }

    private void StartDiskWatcherUnderLock(string workspaceFilePath)
    {
        StopDiskWatcherUnderLock();
        var directory = Path.GetDirectoryName(Path.GetFullPath(workspaceFilePath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            _logger.LogDebug("Disk watcher not started: workspace directory missing ({Path}).", workspaceFilePath);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory)
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

            watcher.Changed += OnDiskWatcherChanged;
            watcher.Created += OnDiskWatcherChanged;
            watcher.Deleted += OnDiskWatcherChanged;
            watcher.Renamed += OnDiskWatcherRenamed;
            watcher.Error += OnDiskWatcherError;
            watcher.EnableRaisingEvents = true;
            _diskWatcher = watcher;
            _logger.LogInformation("Disk watcher started on {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disk watcher failed to start for {Directory}. Symbol search stays on the load snapshot until reset_workspace.", directory);
        }
    }

    private void StopDiskWatcherUnderLock()
    {
        var watcher = _diskWatcher;
        if (watcher is null)
        {
            return;
        }

        _diskWatcher = null;
        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
        }

        watcher.Changed -= OnDiskWatcherChanged;
        watcher.Created -= OnDiskWatcherChanged;
        watcher.Deleted -= OnDiskWatcherChanged;
        watcher.Renamed -= OnDiskWatcherRenamed;
        watcher.Error -= OnDiskWatcherError;
        watcher.Dispose();
    }

    private void OnDiskWatcherChanged(object sender, FileSystemEventArgs e)
    {
        QueueDiskPath(e.FullPath);
    }

    private void OnDiskWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (Directory.Exists(e.FullPath) || Directory.Exists(e.OldFullPath))
        {
            _refreshAllDocuments = true;
            _logger.LogInformation("Disk watcher: directory rename, will refresh known documents on next semantic call.");
            return;
        }

        QueueDiskPath(e.OldFullPath);
        QueueDiskPath(e.FullPath);
    }

    private void OnDiskWatcherError(object sender, ErrorEventArgs e)
    {
        _refreshAllDocuments = true;
        var ex = e.GetException();
        _logger.LogWarning(
            ex,
            "Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.");
    }

    private void QueueDiskPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || WorkspaceDiskPathFilter.IsIgnoredPath(rawPath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception)
        {
            return;
        }

        if (IsSelfWriteSuppressed(fullPath))
        {
            return;
        }

        if (WorkspaceDiskPathFilter.IsProjectGraphFile(fullPath))
        {
            _projectGraphStale = true;
            _logger.LogInformation("Project graph file changed on disk: {Path}", fullPath);
            return;
        }

        if (!WorkspaceDiskPathFilter.IsCSharpSource(fullPath))
        {
            return;
        }

        _dirtySourcePaths.TryAdd(fullPath, 0);
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

    private string? FindClosestSolutionOrProject(string fullFilePath)
    {
        var directoryPath = Path.GetDirectoryName(fullFilePath);
        if (directoryPath is null)
        {
            return null;
        }

        var currentDirectory = new DirectoryInfo(directoryPath);
        while (currentDirectory is not null)
        {
            var solutionPath = Directory
                .EnumerateFiles(currentDirectory.FullName, "*.sln", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (solutionPath is not null)
            {
                return solutionPath;
            }

            var slnxPath = Directory
                .EnumerateFiles(currentDirectory.FullName, "*.slnx", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (slnxPath is not null)
            {
                return slnxPath;
            }

            var projectPath = Directory
                .EnumerateFiles(currentDirectory.FullName, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (projectPath is not null)
            {
                return projectPath;
            }

            currentDirectory = currentDirectory.Parent;
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
}
