using System.Collections;
using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed class DtbEventCounter : ILogger
{
    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Diagnostic;
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        lock (_gate)
        {
            if (_eventSource is not null || _closedHydratePath || _remoteRequested)
            {
                _loggingFailed = true;
                throw new InvalidOperationException("The counter already owns a logging source or a closed hydrate path.");
            }
            _eventSource = eventSource;
            _binlogOrdinal++;
            _attached = true;
            _shutdown = false;
            eventSource.ProjectStarted += OnProjectStarted;
            eventSource.TargetStarted += OnTargetStarted;
            if (string.IsNullOrWhiteSpace(Parameters))
            {
                _loggingFailed = true;
                return;
            }
            try
            {
                var rawPath = Parameters.Split(';')[0];
                if (!Path.IsPathFullyQualified(rawPath))
                {
                    throw new InvalidDataException("The raw binlog path must be absolute.");
                }
                _evidencePaths.Add(Path.GetFullPath(rawPath));
                _binaryLogger = new BinaryLogger { Parameters = Parameters, Verbosity = Verbosity };
                _binaryLogger.Initialize(eventSource);
            }
            catch
            {
                _loggingFailed = true;
                try
                {
                    _binaryLogger?.Shutdown();
                }
                catch
                {
                    // Preserve the original attach failure; the measurement is already incomplete.
                }
                _binaryLogger = null;
                _shutdown = true;
                DetachEvents();
                throw;
            }
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            if (_remoteRequested && !_shutdown)
            {
                _loggingFailed = true;
                _shutdown = true;
                return;
            }
            if (_shutdown || !_attached)
            {
                return;
            }
            try
            {
                _binaryLogger?.Shutdown();
                if (_attached && _binaryLogger is null)
                {
                    _loggingFailed = true;
                }
                _shutdown = _attached;
            }
            catch
            {
                _loggingFailed = true;
                throw;
            }
            finally
            {
                _shutdown = _attached;
                DetachEvents();
                _binaryLogger = null;
            }
        }
    }

    public void RecordOpenInvocation()
    {
        lock (_gate)
        {
            if (_closedHydratePath)
            {
                _loggingFailed = true;
                throw new InvalidOperationException("An ordinary open cannot belong to a closed hydrate path.");
            }
            _openInvocations++;
        }
    }

    public DtbMeasurement Snapshot()
    {
        lock (_gate)
        {
            var complete = !_loggingFailed && (_closedHydratePath || (_attached && _shutdown && _contextsKnown
                && _evidencePaths.Count != 0 && _evidencePaths.All(File.Exists)));
            var evidence = _evidencePaths.Order(StringComparer.Ordinal).ToImmutableArray();
            return new DtbMeasurement(_openInvocations, _projectStarts, _coreCompileTargets,
                complete ? _contexts.Count : null, complete, evidence);
        }
    }

    internal BinaryLogger CreateRemoteBinaryLogger()
    {
        lock (_gate)
        {
            if (_attached || _closedHydratePath || _remoteRequested || string.IsNullOrWhiteSpace(Parameters))
            {
                _loggingFailed = true;
                throw new InvalidOperationException("The remote counter requires one unopened path and explicit raw logger parameters.");
            }
            var rawPath = Parameters.Split(';')[0];
            if (!Path.IsPathFullyQualified(rawPath))
            {
                _loggingFailed = true;
                throw new InvalidDataException("The raw binlog path must be absolute.");
            }
            _remoteDirectory = Path.GetDirectoryName(Path.GetFullPath(rawPath));
            _remoteRequested = true;
            // Roslyn's remote host forwards only the exact BinaryLogger type. A wrapper ILogger is
            // ignored, so the owned counter observes its immutable output through public replay.
            // https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.cs
            return new BinaryLogger { Parameters = Parameters, Verbosity = Verbosity };
        }
    }

    internal void ReplayRawBinlogs(ImmutableArray<string> paths, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_remoteRequested || _replayAttempted || _shutdown)
            {
                _loggingFailed = true;
                throw new InvalidOperationException("Raw replay requires one active remote logger stage.");
            }
            _replayAttempted = true;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (paths.IsDefaultOrEmpty)
            {
                throw new InvalidDataException("The ordinary open produced no raw binlog evidence.");
            }
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var uniquePaths = new HashSet<string>(pathComparer);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (path is null || !Path.IsPathFullyQualified(path) || !uniquePaths.Add(Path.GetFullPath(path))
                    || !pathComparer.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), _remoteDirectory)
                    || !string.Equals(Path.GetExtension(path), ".binlog", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Raw binlog evidence must be distinct files in the owned logger directory.");
                }
            }
            foreach (var path in uniquePaths.Order(pathComparer).ThenBy(path => path, StringComparer.Ordinal))
            {
                ReplayOne(path, cancellationToken);
            }
            lock (_gate)
            {
                _attached = true;
                _shutdown = true;
            }
        }
        catch
        {
            lock (_gate)
            {
                _loggingFailed = true;
                _shutdown = true;
            }
            throw;
        }
    }

    /// <summary>
    /// Marks only the owned hydrate call graph whose absence of Open and process entrypoints is
    /// checked structurally by the experiment fixture. It does not claim process-wide coverage.
    /// </summary>
    internal void MarkHydrateClosedPath()
    {
        lock (_gate)
        {
            if (_attached || _remoteRequested || _openInvocations != 0 || _projectStarts != 0 || _coreCompileTargets != 0)
            {
                _loggingFailed = true;
                throw new InvalidOperationException("The counter is not an unopened hydrate path.");
            }
            _closedHydratePath = true;
        }
    }

    internal void MarkCoverageUnknown()
    {
        lock (_gate)
        {
            _closedHydratePath = false;
            _loggingFailed = true;
        }
    }

    private void ReplayOne(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _binlogOrdinal++;
            _evidencePaths.Add(path);
        }
        var replay = new BinaryLogReplayEventSource();
        replay.ProjectStarted += OnProjectStarted;
        replay.TargetStarted += OnTargetStarted;
        var teePath = Path.Combine(_remoteDirectory!, $"counter-replay-{_binlogOrdinal}-{Guid.NewGuid():N}.binlog");
        var tee = new BinaryLogger { Parameters = $"{teePath};ProjectImports=None", Verbosity = Verbosity };
        Exception? replayFailure = null;
        try
        {
            // A distinct replay destination preserves the remote host's original raw log bytes.
            // Paths are collected before replay, so these diagnostic copies cannot be replayed twice.
            tee.Initialize(replay);
            lock (_gate)
            {
                _evidencePaths.Add(teePath);
            }
            using var stream = File.OpenRead(path);
            replay.Replay(stream, cancellationToken);
        }
        catch (Exception exception)
        {
            replayFailure = exception;
            throw;
        }
        finally
        {
            replay.ProjectStarted -= OnProjectStarted;
            replay.TargetStarted -= OnTargetStarted;
            try
            {
                tee.Shutdown();
            }
            catch when (replayFailure is not null)
            {
                // Keep the original failure, particularly cancellation with the caller's token.
            }
        }
    }

    private void OnProjectStarted(object sender, ProjectStartedEventArgs args)
    {
        lock (_gate)
        {
            _projectStarts++;
            var context = args.BuildEventContext;
            // An owned MSBuildWorkspace Open invocation establishes design-time request association
            // even when ProjectStarted.Properties is empty (the observed Roslyn host behavior).
            var designTime = _openInvocations > 0 || HasDesignTimeFlag(args.Properties)
                || HasDesignTimeFlag(args.GlobalProperties);
            if (context is null || !designTime)
            {
                _contextsKnown = false;
                return;
            }
            _contexts.Add((_binlogOrdinal, context.SubmissionId, context.NodeId,
                context.ProjectContextId, context.ProjectInstanceId));
        }
    }

    private void OnTargetStarted(object sender, TargetStartedEventArgs args)
    {
        lock (_gate)
        {
            if (string.Equals(args.TargetName, "CoreCompile", StringComparison.Ordinal))
            {
                _coreCompileTargets++;
            }
        }
    }

    private static bool HasDesignTimeFlag(IEnumerable? properties)
    {
        if (properties is null)
        {
            return false;
        }
        foreach (var property in properties)
        {
            if (property is DictionaryEntry entry
                && string.Equals(entry.Key?.ToString(), "DesignTimeBuild", StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Value?.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (property is KeyValuePair<string, string> pair
                && string.Equals(pair.Key, "DesignTimeBuild", StringComparison.OrdinalIgnoreCase)
                && string.Equals(pair.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private void DetachEvents()
    {
        if (_eventSource is null)
        {
            return;
        }
        _eventSource.ProjectStarted -= OnProjectStarted;
        _eventSource.TargetStarted -= OnTargetStarted;
        _eventSource = null;
    }

    private readonly object _gate = new();
    private readonly HashSet<(int Binlog, int Submission, int Node, int ProjectContext, int ProjectInstance)> _contexts = [];
    private readonly HashSet<string> _evidencePaths = new(StringComparer.Ordinal);
    private IEventSource? _eventSource;
    private BinaryLogger? _binaryLogger;
    private int _binlogOrdinal;
    private int _openInvocations;
    private int _projectStarts;
    private int _coreCompileTargets;
    private bool _attached;
    private bool _shutdown;
    private bool _contextsKnown = true;
    private bool _loggingFailed;
    private bool _closedHydratePath;
    private string? _remoteDirectory;
    private bool _remoteRequested;
    private bool _replayAttempted;
}
