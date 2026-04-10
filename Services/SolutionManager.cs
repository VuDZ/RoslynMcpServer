using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Serilog;

public sealed class SolutionManager
{
    /// <summary>
    /// Working set threshold (bytes) above which a memory warning is emitted.
    /// </summary>
    private const long MemoryWarningThresholdBytes = 1500L * 1024 * 1024;

    /// <summary>
    /// Explicit MEF host so MSBuildWorkspace discovers the C# language / project loader
    /// (fixes "language 'C#' is not supported" when using parameterless MSBuildWorkspace.Create()).
    /// </summary>
    private static readonly HostServices MsBuildHostServices = MefHostServices.Create(
        new[]
        {
            typeof(Workspace).Assembly,
            typeof(CSharpFormattingOptions).Assembly,
            typeof(MSBuildWorkspace).Assembly
        });

    private readonly ILogger<SolutionManager> _logger;
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private string? _loadedPath;
    private IReadOnlyList<WorkspaceDiagnostic> _lastDiagnostics = Array.Empty<WorkspaceDiagnostic>();

    public SolutionManager(ILogger<SolutionManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<WorkspaceDiagnostic> LastDiagnostics => _lastDiagnostics;

    public async Task<Solution> LoadAsync(string path)
    {
        return await LoadAsync(path, CancellationToken.None);
    }

    public async Task<Solution> LoadAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken)
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

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(fullPath, cancellationToken);
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

        var fullFilePath = Path.GetFullPath(filePath);
        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureWorkspaceLoadedForFileUnderLockAsync(fullFilePath, cancellationToken);

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

    public Solution? GetCurrentSolution()
    {
        return _workspace?.CurrentSolution ?? _solution;
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
            _workspace?.Dispose();
            _workspace = null;
            _solution = null;
            _loadedPath = null;
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
                "Could not locate a .sln or .csproj while walking parent directories.",
                fullFilePath);
        }

        var candidateFull = Path.GetFullPath(candidate);
        if (!File.Exists(candidateFull))
        {
            throw new FileNotFoundException("Solution or project file not found.", candidateFull);
        }

        _ = await LoadCoreAsync(candidateFull, cancellationToken);
    }

    /// <summary>
    /// Loads or returns cached solution. Caller must hold <see cref="_workspaceLock"/>.
    /// </summary>
    private async Task<Solution> LoadCoreAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (_workspace is not null && string.Equals(_loadedPath, fullPath, _pathComparison))
        {
            var cached = _solution ?? _workspace.CurrentSolution;
            LogProcessWorkingSet("workspace_load_cached");
            return cached;
        }

        _workspace?.Dispose();
        _ = typeof(CSharpFormattingOptions).Assembly.FullName;
        var workspace = MSBuildWorkspace.Create(MsBuildHostServices);
        var capturedDiagnostics = new List<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            capturedDiagnostics.Add(e.Diagnostic);
            _logger.LogWarning("MSBuildWorkspace warning: {Message}", e.Diagnostic.Message);
        });

        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".sln", _pathComparison))
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
            throw new NotSupportedException("Only .sln and .csproj files are supported.");
        }

        _workspace = workspace;
        _solution = workspace.CurrentSolution;
        _loadedPath = fullPath;
        _lastDiagnostics = CollectDiagnostics(workspace, capturedDiagnostics);
        _logger.LogInformation("Loaded Roslyn workspace from {Path}", fullPath);
        LogProcessWorkingSet("workspace_load");
        return _solution;
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
