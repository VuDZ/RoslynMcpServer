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

    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private string? _loadedPath;
    private string? _loadedConfiguration;
    private string? _loadedPlatform;
    private string? _loadedTargetFramework;
    private IReadOnlyList<WorkspaceDiagnostic> _lastDiagnostics = Array.Empty<WorkspaceDiagnostic>();

    public SolutionManager(ILogger<SolutionManager> logger)
    {
        _logger = logger;
        _dirtySourcePaths = new ConcurrentDictionary<string, byte>(_pathComparer);
        _selfWriteUntilTicks = new ConcurrentDictionary<string, long>(_pathComparer);
    }

    public IReadOnlyList<WorkspaceDiagnostic> LastDiagnostics => _lastDiagnostics;

    /// <summary>MSBuild <c>Configuration</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedConfiguration => _loadedConfiguration;

    /// <summary>MSBuild <c>Platform</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedPlatform => _loadedPlatform;

    /// <summary>MSBuild <c>TargetFramework</c> used for the last successful <see cref="LoadAsync"/>, or <see langword="null"/>.</summary>
    public string? LoadedTargetFramework => _loadedTargetFramework;

    public async Task<Solution> LoadAsync(string path)
    {
        return await LoadAsync(path, CancellationToken.None);
    }

    public async Task<Solution> LoadAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken,
        string? configuration = null,
        string? platform = null,
        string? targetFramework = null)
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

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(
                fullPath,
                normalizedConfiguration,
                normalizedPlatform,
                normalizedTargetFramework,
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

            _solution = workspace.CurrentSolution;
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

            return workspace.CurrentSolution.Projects
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
        return _workspace?.CurrentSolution ?? _solution;
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
            if (workspace is not null && workspace.TryApplyChanges(newSolution))
            {
                _solution = workspace.CurrentSolution;
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
            _solution = workspace.CurrentSolution;
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
            _loadedPath = null;
            _loadedConfiguration = null;
            _loadedPlatform = null;
            _loadedTargetFramework = null;
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
            await FlushDirtyDocumentsUnderLockAsync(cancellationToken).ConfigureAwait(false);
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
        _solution = workspace.CurrentSolution;
        _loadedPath = fullPath;
        _loadedConfiguration = configuration;
        _loadedPlatform = platform;
        _loadedTargetFramework = targetFramework;
        _lastDiagnostics = CollectDiagnostics(workspace, capturedDiagnostics);
        StartDiskWatcherUnderLock(fullPath);
        _logger.LogInformation(
            "Loaded Roslyn workspace from {Path} (Configuration={Configuration}, Platform={Platform}, TargetFramework={TargetFramework})",
            fullPath,
            configuration ?? "(default)",
            platform ?? "(default)",
            targetFramework ?? "(default)");
        LogProcessWorkingSet("workspace_load");
        return _solution;
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

        _solution = workspace.CurrentSolution;
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
