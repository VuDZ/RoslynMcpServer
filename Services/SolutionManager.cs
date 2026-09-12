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
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Serilog;

internal sealed record WorkspaceLoadPreparationResult(
    Solution Solution,
    IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ShadowCopyResults);

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
    private readonly AnalyzerProvenanceCaptureService _analyzerProvenanceCaptureService;
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
    // _lastRefreshStale/_lastShadowCopyResults are the last refresh, and
    // _shadowCopyAnalyzersEnabled is session-sticky active-overlay state.
    // _lastExecutionObservation is epoch-3 prepared/rewritten/load-failed/execution/restart.
    // Holding mapping for an in-flight write is epoch 4.
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
    private AnalyzerExecutionObservation _lastExecutionObservation = AnalyzerExecutionObservation.None;
    private readonly ConditionalWeakTable<Solution, WorkspaceWriteOperationContext> _operationContexts = new();
    private WorkspaceWriteOperationContext? _lastPublishedWriteContext;
    private WorkspaceWriteResult? _lastWriteResult;
    private long _rawWorkspaceRevision;
    private string? _loadedPath;
    private string? _loadedConfiguration;
    private string? _loadedPlatform;
    private string? _loadedTargetFramework;
    private string? _loadedBuildArgs;
    private IReadOnlyList<WorkspaceDiagnostic> _lastDiagnostics = Array.Empty<WorkspaceDiagnostic>();
    private AnalyzerProvenanceSnapshot? _analyzerProvenanceSnapshot;
    private long _analyzerProvenanceCaptureCount;

    public SolutionManager(
        ILogger<SolutionManager> logger,
        AnalyzerProvenanceCaptureService analyzerProvenanceCaptureService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(analyzerProvenanceCaptureService);

        _logger = logger;
        _analyzerProvenanceCaptureService = analyzerProvenanceCaptureService;
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

    internal AnalyzerExecutionObservation LastExecutionObservation => _lastExecutionObservation;

    /// <summary>
    /// First-use observation: maps a load/execution failure to project, generator,
    /// generation, and missing/conflicting dependency. Loading stays lazy until this
    /// or a semantic compilation runs.
    /// </summary>
    internal AnalyzerExecutionObservation ObserveAnalyzerExecution(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _lastExecutionObservation = AnalyzerExecutionGate.ObserveFirstUse(
            project,
            _lastExecutionObservation,
            _analyzerAssemblyLoader);
        return _lastExecutionObservation;
    }

    internal bool ShadowCopyAnalyzersEnabled => _shadowCopyAnalyzersEnabled;

    internal string? ShadowCopyRootDirectory => _shadowCopyRootDirectory;

    internal AnalyzerShadowMapping? AnalyzerShadowMapping => _analyzerShadowMapping;

    internal Guid LoadSessionId => _loadSessionId;

    internal AnalyzerProvenanceSnapshot? AnalyzerProvenanceSnapshot => _analyzerProvenanceSnapshot;

    internal long AnalyzerProvenanceCaptureCount => _analyzerProvenanceCaptureCount;

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

    internal WorkspaceWriteResult? LastWriteResult => _lastWriteResult;

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

    internal async Task<WorkspaceLoadPreparationResult> LoadAndPrepareAsync(
        string solutionOrProjectPath,
        bool shadowCopyInSolutionAnalyzers,
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
            var publishedBeforeBoundary = _solution;
            Solution solution;
            try
            {
                solution = await LoadCoreAsync(
                        fullPath,
                        normalizedConfiguration,
                        normalizedPlatform,
                        normalizedTargetFramework,
                        normalizedBuildArgs,
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

            if (!shadowCopyInSolutionAnalyzers)
            {
                return new WorkspaceLoadPreparationResult(solution, _lastShadowCopyResults);
            }

            if (_lastLoadWasCacheHit)
            {
                _solution = publishedBeforeBoundary;
            }

            if (HasBlockingLoadFailure(solution))
            {
                _solution = null;
                return new WorkspaceLoadPreparationResult(solution, Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>());
            }

            try
            {
                var boundarySeam = AfterPhysicalLoadBeforePrepareAsync;
                if (boundarySeam is not null)
                {
                    await boundarySeam(cancellationToken).ConfigureAwait(false);
                }

                var results = PrepareInSolutionAnalyzerReferencesUnderLock();
                if (!_shadowCopyAnalyzersEnabled
                    && _analyzerShadowMapping is not { HasAnyApplied: true })
                {
                    SetFailClosedPublishedSolution(_workspace!.CurrentSolution);
                }

                return new WorkspaceLoadPreparationResult(_solution!, results);
            }
            catch
            {
                RestoreSafePublishedSnapshotAfterOptInFailure();
                throw;
            }
        }
        finally
        {
            _workspaceLock.Release();
        }
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

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> PrepareInSolutionAnalyzerReferencesUnderLock()
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
            loadedPath,
            _analyzerProvenanceSnapshot);
        return CompletePrepare(workspace.CurrentSolution, shadowRoot, prepared);
    }

    /// <summary>
    /// Reapplies the stored analyzer-reference mapping on top of <paramref name="solution"/> with no analyzer
    /// file I/O, then applies the epoch-3 binding gate (identity collision / unsupported helpers).
    /// Every caller that would otherwise cache <c>workspace.CurrentSolution</c> into <c>_solution</c>
    /// must route through here so the overlay survives document edit / disk sync. A no-op when this load
    /// session has no mapping and the gate has nothing to block.
    /// </summary>
    private Solution PublishInMemorySolution(Solution solution)
    {
        if (_lastExecutionObservation.RequiresRestart)
        {
            return AnalyzerExecutionGate.StripInSolutionAnalyzerReferences(
                solution,
                _analyzerProvenanceSnapshot,
                _loadSessionId);
        }

        solution = ApplyShadowCopyOverlayIfEnabled(solution);
        if (!_lastExecutionObservation.PermitsExecution)
        {
            solution = AnalyzerExecutionGate.BlockUnsupportedReferences(
                solution,
                _analyzerProvenanceSnapshot,
                _loadSessionId,
                _analyzerAssemblyLoader);
        }

        return solution;
    }

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
        var gate = EvaluatePreparedMapping(prepared.Mapping);
        if (gate.Status == AnalyzerExecutionStatus.None && !_lastExecutionObservation.PermitsExecution)
        {
            gate = _lastExecutionObservation;
        }

        _lastExecutionObservation = gate;
        _lastRefreshStale = prepared.UsedPreviousMappingAsStale
            || prepared.Mapping.Entries.Any(e => e.StaleGeneration)
            || !gate.PermitsExecution;

        var results = ToGatedRewriteResults(prepared.Results, gate);
        _lastShadowCopyResults = results;

        if (gate.PermitsExecution && prepared.Mapping.HasAnyApplied)
        {
            _analyzerShadowMapping = prepared.Mapping;
            _shadowCopyAnalyzersEnabled = true;
            _shadowCopyRootDirectory = shadowRoot;
            _lastExecutionObservation = gate.WithStage(
                AnalyzerPreparationStage.ReferenceRewritten,
                AnalyzerExecutionStatus.ReferenceRewritten);
            SetPublishedSolution(workspaceSolution);
            return results;
        }

        if (!gate.PermitsExecution && _analyzerShadowMapping is { HasAnyApplied: true } previous)
        {
            var stale = previous.WithStale(gate.Reason ?? AnalyzerLoaderContract.RestartRequiredReason);
            _analyzerShadowMapping = stale;
            _lastRefreshStale = true;
            _shadowCopyRootDirectory = shadowRoot;
            // Restart-required must not keep stale V1 in compilation (A3-01).
            // File-prepare failure still reapplies the previous mapping (epoch 2).
            _shadowCopyAnalyzersEnabled = !gate.RequiresRestart;
            SetPublishedSolution(workspaceSolution);
            return results;
        }

        if (prepared.Mapping.HasAnyApplied && !gate.PermitsExecution)
        {
            _analyzerShadowMapping = null;
            _shadowCopyAnalyzersEnabled = false;
            _shadowCopyRootDirectory = shadowRoot;
            SetPublishedSolution(workspaceSolution);
        }

        return results;
    }

    private AnalyzerExecutionObservation EvaluatePreparedMapping(AnalyzerShadowMapping mapping)
    {
        AnalyzerExecutionObservation? firstBlock = null;
        AnalyzerExecutionObservation? firstPrepared = null;
        foreach (var entry in mapping.Entries)
        {
            var observation = AnalyzerExecutionGate.EvaluatePreparedEntry(entry, _analyzerAssemblyLoader);
            if (!observation.PermitsExecution)
            {
                firstBlock ??= observation;
            }
            else if (observation.Status == AnalyzerExecutionStatus.Prepared)
            {
                firstPrepared ??= observation;
            }
        }

        return firstBlock ?? firstPrepared ?? AnalyzerExecutionObservation.None;
    }

    private static IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ToGatedRewriteResults(
        IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> results,
        AnalyzerExecutionObservation gate)
    {
        if (gate.PermitsExecution)
        {
            return results;
        }

        return results.Select(result =>
        {
            if (!result.Applied)
            {
                return result;
            }

            return result with
            {
                Applied = false,
                SkipReason = gate.Reason ?? AnalyzerLoaderContract.RestartRequiredReason,
                StaleGeneration = true,
                ReasonCode = AnalyzerReferenceReasonCodes.PreparationFailure,
            };
        }).ToList();
    }

    private IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> CompleteFailedPrepare(
        Solution workspaceSolution,
        string shadowRoot,
        string reason)
    {
        _lastExecutionObservation = new AnalyzerExecutionObservation
        {
            Status = AnalyzerExecutionStatus.LoadFailed,
            HighestStage = AnalyzerPreparationStage.LoadFailed,
            Reason = reason,
            ProjectName = _analyzerShadowMapping?.Entries.FirstOrDefault()?.ProjectName,
            GeneratorName = _analyzerShadowMapping?.Entries.FirstOrDefault()?.MatchedProjectName,
            GenerationId = _analyzerShadowMapping?.Entries.FirstOrDefault()?.GenerationId,
        };

        if (_analyzerShadowMapping is { HasAnyApplied: true } previous)
        {
            var stale = previous.WithStale(reason, AnalyzerReferenceReasonCodes.PreparationFailure);
            _analyzerShadowMapping = stale;
            _lastShadowCopyResults = AnalyzerReferenceShadowCopier.ToRewriteResults(stale);
            _lastRefreshStale = true;
            _shadowCopyAnalyzersEnabled = true;
            _shadowCopyRootDirectory = shadowRoot;
            SetPublishedSolution(workspaceSolution);
            return _lastShadowCopyResults;
        }

        _lastShadowCopyResults = Array.Empty<AnalyzerReferenceShadowCopier.RewriteResult>();
        _lastRefreshStale = false;
        SetFailClosedPublishedSolution(workspaceSolution);
        return _lastShadowCopyResults;
    }

    private void SetPublishedSolution(Solution workspaceSolution)
    {
        var overlay = PublishInMemorySolution(workspaceSolution);
        SetPublishedSnapshot(overlay);
    }

    private Solution CreateFailClosedSnapshot(Solution workspaceSolution) =>
        AnalyzerExecutionGate.StripInSolutionAnalyzerReferences(
            workspaceSolution,
            _analyzerProvenanceSnapshot,
            _loadSessionId);

    private void SetFailClosedPublishedSolution(Solution workspaceSolution)
    {
        _shadowCopyAnalyzersEnabled = false;
        SetPublishedSnapshot(CreateFailClosedSnapshot(workspaceSolution));
    }

    private void RestoreSafePublishedSnapshotAfterOptInFailure()
    {
        if (_workspace is null || _analyzerProvenanceSnapshot is null)
        {
            _solution = null;
            return;
        }

        if (_shadowCopyAnalyzersEnabled
            && _analyzerShadowMapping is { HasAnyApplied: true })
        {
            SetPublishedSolution(_workspace.CurrentSolution);
            return;
        }

        SetFailClosedPublishedSolution(_workspace.CurrentSolution);
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
            _loadSessionId,
            _loadedPath,
            _analyzerShadowMapping,
            publishedBase,
            rawWorkspace,
            _rawWorkspaceRevision,
            _shadowCopyAnalyzersEnabled);
    }

    private WorkspaceWriteFreshnessState CurrentWriteFreshness(Solution rawWorkspace)
    {
        return new WorkspaceWriteFreshnessState(
            _loadSessionId,
            _loadedPath,
            _analyzerShadowMapping,
            _shadowCopyAnalyzersEnabled,
            _rawWorkspaceRevision,
            rawWorkspace,
            _solution);
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
        _lastWriteResult = result;
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

    /// <summary>
    /// Waits for an in-flight load/prepare boundary and returns only its published snapshot.
    /// It never falls back to raw <see cref="Workspace.CurrentSolution"/>.
    /// </summary>
    public async Task<Solution?> GetPublishedSolutionAsync(CancellationToken cancellationToken = default)
    {
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
    /// </summary>
    public async Task<Solution?> GetPublishedSolutionAfterDiskSyncAsync(CancellationToken cancellationToken = default)
    {
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

        var baseSolution = workspace.CurrentSolution;
        var candidate = baseSolution.WithDocumentText(
            documentId,
            SourceText.From(newText, Encoding.UTF8));
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
            _analyzerAssemblyLoader,
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
                var text = (await newDoc.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                if (oldDoc is not null)
                {
                    var oldText = (await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                    if (string.Equals(oldText, text, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var fullPath = Path.GetFullPath(newDoc.FilePath);
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

                SuppressDiskWatchForPath(fullPath);
                await File.WriteAllTextAsync(fullPath, text, cancellationToken).ConfigureAwait(false);
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
            var documentId = FindDocumentIdForPath(current, Path.GetFullPath(path), _pathComparison);
            if (documentId is null)
            {
                return false;
            }

            current = current.WithDocumentText(documentId, SourceText.From(text, Encoding.UTF8));
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
            _lastPublishedWriteContext = null;
            _lastWriteResult = null;
            NoteRawWorkspaceRevision();
            _shadowCopyAnalyzersEnabled = false;
            _shadowCopyRootDirectory = null;
            _analyzerShadowMapping = null;
            _analyzerProvenanceSnapshot = null;
            _loadSessionId = Guid.Empty;
            _lastRefreshStale = false;
            _lastExecutionObservation = AnalyzerExecutionObservation.None;
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
    /// Loads or returns cached solution. Caller must hold <see cref="_workspaceLock"/>.
    /// </summary>
    private async Task<Solution> LoadCoreAsync(
        string fullPath,
        string? configuration,
        string? platform,
        string? targetFramework,
        string? buildArgs,
        bool publishLoadedSolution,
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
        _workspace = null;
        _solution = null;
        _loadedPath = null;
        _loadedConfiguration = null;
        _loadedPlatform = null;
        _loadedTargetFramework = null;
        _loadedBuildArgs = null;
        _dirtySourcePaths.Clear();
        _selfWriteUntilTicks.Clear();
        _refreshAllDocuments = false;
        _projectGraphStale = false;
        _shadowCopyAnalyzersEnabled = false;
        _shadowCopyRootDirectory = null;
        _analyzerShadowMapping = null;
        _analyzerProvenanceSnapshot = null;
        _lastPublishedWriteContext = null;
        _lastWriteResult = null;
        _loadSessionId = Guid.NewGuid();
        _lastRefreshStale = false;
        _lastExecutionObservation = AnalyzerExecutionObservation.None;
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

        AnalyzerProvenanceSnapshot provenanceSnapshot;
        try
        {
            _analyzerProvenanceCaptureCount++;
            provenanceSnapshot = await _analyzerProvenanceCaptureService.OpenAndCaptureAsync(
                    workspace,
                    fullPath,
                    _loadSessionId,
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
        _analyzerProvenanceSnapshot = provenanceSnapshot;
        _lastExecutionObservation = AnalyzerExecutionGate.EvaluateInSolutionAnalyzers(
            workspace.CurrentSolution,
            provenanceSnapshot,
            _loadSessionId,
            _analyzerAssemblyLoader);
        if (publishLoadedSolution)
        {
            SetPublishedSolution(workspace.CurrentSolution);
        }
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
        return publishLoadedSolution ? _solution ?? workspace.CurrentSolution : workspace.CurrentSolution;
    }

    private bool HasBlockingLoadFailure(Solution solution)
    {
        if (!solution.Projects.Any())
        {
            return true;
        }

        return _lastDiagnostics
            .Select(diagnostic => WorkspaceDiagnosticFormatter.Format(
                diagnostic.Kind.ToString(),
                diagnostic.Message))
            .Any(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure);
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

        if (result.Updated == 0
            && result.Added == 0
            && result.Removed == 0
            && ReferenceEquals(result.Solution, workspace.CurrentSolution))
        {
            if (dirty.Count > 0 || refreshAll)
            {
                NoteRawWorkspaceRevision();
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
                "workspace_disk_sync write {Status} reason={Reason} updated={Updated} added={Added} removed={Removed}.",
                write.Status,
                write.Reason,
                result.Updated,
                result.Added,
                result.Removed);
            return;
        }

        _logger.LogInformation(
            "workspace_disk_sync updated={Updated} added={Added} removed={Removed} unchanged={Unchanged} refreshAll={RefreshAll} elapsedMs={ElapsedMs} write={WriteStatus}",
            result.Updated,
            result.Added,
            result.Removed,
            result.Unchanged,
            refreshAll,
            started.ElapsedMilliseconds,
            write.Status);
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
