using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics.UsageStatistics.Models;
using RoslynMcpServer.Hosting;

namespace RoslynMcpServer.Diagnostics.UsageStatistics;

/// <summary>Best-effort local usage events; producers never perform file or process I/O.</summary>
public sealed class UsageStatisticsService : IHostedService, IDisposable
{
    internal string? WorkspaceKey
    {
        get
        {
            lock (_gate)
            {
                return _workspaceKey;
            }
        }
    }

    public UsageStatisticsService(ILogger<UsageStatisticsService> logger, IOptions<McpToolProfileOptions> options)
        : this(
            ServerUserDirectories.FromEnvironment().UsageDirectory,
            IsEnabled(Environment.GetEnvironmentVariable("ROSLYN_MCP_USAGE")),
            logger,
            profile: options.Value.Profile)
    {
    }

    internal UsageStatisticsService(
        string directory,
        bool enabled = true,
        ILogger<UsageStatisticsService>? logger = null,
        TimeProvider? timeProvider = null,
        string? profile = null,
        long maximumFileBytes = UsageJsonlWriter.DefaultMaximumFileBytes)
    {
        _enabled = enabled;
        _logger = logger ?? NullLogger<UsageStatisticsService>.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _profile = profile ?? "full";
        _writer = new UsageJsonlWriter(directory, maximumFileBytes);
        _lastActivity = _time.GetTimestamp();
        _version = typeof(UsageStatisticsService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        try
        {
            if (_enabled)
            {
                var parent = ParentProcessProbe.Capture();
                _harness = HarnessNameResolver.Resolve(parent.ImagePath, parent.CommandLine);
                _parentId = OperatingSystem.IsWindows()
                    ? WindowsParentProcess.TryGetParentProcessId()
                    : OperatingSystem.IsLinux() ? LinuxParentProcess.TryGetParentProcessId() : 0;
            }
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _writerTask = Task.Run(WriteLoopAsync, CancellationToken.None);
        Emit("start", new()
        {
            ["cwd"] = Environment.CurrentDirectory,
            ["gc"] = GCSettings.IsServerGC ? "server" : "workstation",
            ["cores"] = Environment.ProcessorCount,
            ["profile"] = _profile,
        });
        _samplerTask = Task.Run(SampleLoopAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_enabled || Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _samplerCancellation.Cancel();
        Emit("exit", new() { ["reason"] = _exitReason });
        _events.Writer.TryComplete();
        try
        {
            await Task.WhenAll(_writerTask, _samplerTask)
                .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    public void SetExitReason(string reason)
    {
        lock (_gate)
        {
            _exitReason = reason;
        }
    }

    public void Dispose()
    {
        _samplerCancellation.Cancel();
        _events.Writer.TryComplete();
    }

    internal static bool IsEnabled(string? value) =>
        !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    internal UsageToolCall? BeginTool(string name, bool requiresWorkspace)
    {
        if (!_enabled)
        {
            return null;
        }

        lock (_gate)
        {
            var call = new UsageToolCall
            {
                Id = ++_callSequence,
                Name = name,
                Started = _time.GetUtcNow(),
                Timestamp = _time.GetTimestamp(),
                WorkspaceKey = _workspaceKey,
                RequiresWorkspace = requiresWorkspace,
                Parent = _currentCall.Value,
            };
            _currentCall.Value = call;
            _activeCalls++;
            _lastActivity = call.Timestamp;
            Emit("tool_start", ToolFields(call));
            if (name == "stop_mcp_server")
            {
                _exitReason = "stop_mcp_server";
            }

            return call;
        }
    }

    internal void EndTool(UsageToolCall? call, bool success)
    {
        if (call is null)
        {
            return;
        }

        lock (_gate)
        {
            _activeCalls--;
            _lastActivity = _time.GetTimestamp();
            var fields = ToolFields(call);
            fields["duration_ms"] = _time.GetElapsedTime(call.Timestamp).TotalMilliseconds;
            fields["success"] = success;
            fields["lazy_load"] = call.LazyLoad;
            fields["requires_workspace"] = call.RequiresWorkspace || call.LazyLoad;
            Emit("tool", fields);
            _currentCall.Value = call.Parent;
        }
    }

    internal void ClearResidentWorkspace()
    {
        lock (_gate)
        {
            _workspaceKey = null;
        }
    }

    internal void MarkCurrentSolutionAccess()
    {
        if (_currentCall.Value is { } call && !McpToolCatalog.All.Any(tool => tool.Name == call.Name))
        {
            call.RequiresWorkspace = true;
        }
    }

    internal void MarkWorkspaceRequired()
    {
        if (_currentCall.Value is { } call)
        {
            call.RequiresWorkspace = true;
        }
    }

    internal void RecordClient(string? name, string? version, string? protocol)
    {
        lock (_gate)
        {
            var client = (Name: name, Version: version, Protocol: protocol);
            if (_client is { } previous && previous == client)
            {
                return;
            }

            _client = client;
            Emit("client", new() { ["name"] = name, ["version"] = version, ["protocol"] = protocol });
        }
    }

    internal UsageLoadAttempt? BeginLoad(
        string? path,
        string? configuration,
        string? platform,
        string? targetFramework,
        WorkspaceLoadSource source)
    {
        if (!_enabled)
        {
            return null;
        }

        var call = _currentCall.Value;
        MarkWorkspaceRequired();
        var origin = source == WorkspaceLoadSource.ConfigFile
            ? "lazy"
            : call?.Name is "list_projects" or "get_project_graph" ? "path" : "explicit";
        if (call is not null && origin == "lazy")
        {
            call.LazyLoad = true;
        }

        return new UsageLoadAttempt
        {
            Source = origin,
            Started = _time.GetUtcNow(),
            RequestedKey = UsageWorkspaceKey.Create(path, configuration, platform, targetFramework),
        };
    }

    internal void RecordLoad(UsageLoadAttempt attempt, bool success)
    {
        lock (_gate)
        {
            var wait = attempt.WaitMilliseconds
                ?? (attempt.WaitStarted is { } started ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : 0);
            if (attempt.ResidentCaptured)
            {
                _workspaceKey = attempt.ResidentKey;
            }

            if (success)
            {
                _lastLoad = _time.GetTimestamp();
            }

            Emit("load", new()
            {
                ["call"] = _currentCall.Value?.Id,
                ["start_ts"] = attempt.Started,
                ["requested_key"] = attempt.RequestedKey,
                ["workspace_key"] = _workspaceKey,
                ["success"] = success,
                ["cache_hit"] = attempt.CacheHit,
                ["source"] = attempt.Source,
                ["graph_stale"] = attempt.GraphStale,
                ["duration_ms"] = Math.Max(0, attempt.Elapsed.Elapsed.TotalMilliseconds - wait),
                ["lock_wait_ms"] = wait,
                ["projects"] = attempt.Projects,
            });
            attempt.Recorded = true;
        }

        RequestSnapshot();
    }

    internal void RecordReset(string? oldKey, string? residentKey, DateTimeOffset started, bool success)
    {
        lock (_gate)
        {
            var seconds = _lastLoad is { } last ? _time.GetElapsedTime(last).TotalSeconds : (double?)null;
            _workspaceKey = residentKey;
            if (residentKey is null)
            {
                _lastLoad = null;
            }

            Emit("reset", new()
            {
                ["workspace_key"] = oldKey,
                ["resident_key"] = residentKey,
                ["start_ts"] = started,
                ["success"] = success,
                ["seconds_since_load"] = seconds,
            });
        }

        RequestSnapshot();
    }

    internal void SampleMemory()
    {
        var memory = UsageMemorySampler.Capture();
        lock (_gate)
        {
            Emit("mem", new()
            {
                ["workspace_key"] = _workspaceKey,
                ["active_calls"] = _activeCalls,
                ["idle_seconds"] = _activeCalls == 0 ? _time.GetElapsedTime(_lastActivity).TotalSeconds : 0,
                ["memory_bytes"] = memory.MemoryBytes,
                ["buildhost_bytes"] = memory.BuildHostBytes,
                ["buildhost_count"] = memory.BuildHostCount,
                ["working_set_bytes"] = memory.WorkingSetBytes,
                ["managed_heap_bytes"] = memory.ManagedHeapBytes,
                ["committed_bytes"] = memory.CommittedBytes,
            });
        }
    }

    private static Dictionary<string, object?> ToolFields(UsageToolCall call) => new()
    {
        ["call"] = call.Id,
        ["name"] = call.Name,
        ["start_ts"] = call.Started,
        ["workspace_key"] = call.WorkspaceKey,
        ["requires_workspace"] = call.RequiresWorkspace,
    };

    private void Emit(string eventName, Dictionary<string, object?> fields)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                fields["v"] = 1;
                fields["ts"] = _time.GetUtcNow();
                fields["proc"] = _processId;
                fields["seq"] = ++_sequence;
                fields["pid"] = Environment.ProcessId;
                fields["ppid"] = _parentId;
                fields["harness"] = _harness;
                fields["ver"] = _version;
                fields["ev"] = eventName;
                if (fields.Values.Any(value => value is string text && text.Length > UsageJsonlWriter.MaximumRecordBytes))
                {
                    throw new InvalidDataException("Usage field exceeds the record size limit.");
                }

                var json = JsonSerializer.SerializeToUtf8Bytes(fields);
                if (json.Length + 1 > UsageJsonlWriter.MaximumRecordBytes)
                {
                    throw new InvalidDataException("Usage record exceeds the size limit.");
                }

                var record = new byte[json.Length + 1];
                json.CopyTo(record, 0);
                record[^1] = (byte)'\n';
                if (!_events.Writer.TryWrite(record))
                {
                    throw new IOException("Usage event queue is full or closed.");
                }
            }
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var record in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            WritePendingFailure();
            try
            {
                _writer.Append(record, _time.GetUtcNow());
            }
            catch (Exception ex)
            {
                ReportFailure(ex);
            }
        }

        WritePendingFailure();
    }

    private async Task SampleLoopAsync()
    {
        try
        {
            while (!_samplerCancellation.IsCancellationRequested)
            {
                try
                {
                    SampleMemory();
                }
                catch (Exception ex)
                {
                    ReportFailure(ex);
                }

                await _snapshotSignal.WaitAsync(TimeSpan.FromMinutes(1), _samplerCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_samplerCancellation.IsCancellationRequested)
        {
        }
    }

    private void RequestSnapshot()
    {
        if (_snapshotSignal.CurrentCount == 0)
        {
            try
            {
                _snapshotSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private void ReportFailure(Exception exception) =>
        Interlocked.Exchange(ref _pendingFailure, new Failure(exception.GetType().Name, exception.HResult));

    private void WritePendingFailure()
    {
        var failure = Interlocked.Exchange(ref _pendingFailure, null);
        if (failure is null)
        {
            return;
        }

        try
        {
            _logger.LogWarning(
                "Usage statistics failure: {ErrorType} (0x{ErrorCode:X8}).",
                failure.Type,
                failure.Code);
        }
        catch
        {
            // A failed diagnostics sink must not replace the result of a tool.
        }
    }

    private sealed record Failure(string Type, int Code);

    private Failure? _pendingFailure;
    private readonly bool _enabled;
    private readonly ILogger<UsageStatisticsService> _logger;
    private readonly TimeProvider _time;
    private readonly string _profile;
    private readonly UsageJsonlWriter _writer;
    private readonly string _processId = Guid.NewGuid().ToString("N");
    private readonly string _version;
    private readonly string _harness = HarnessNameResolver.Unknown;
    private readonly int _parentId;
    private readonly object _gate = new();
    private readonly AsyncLocal<UsageToolCall?> _currentCall = new();
    private readonly Channel<byte[]> _events = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(1024) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim _snapshotSignal = new(0, 1);
    private readonly CancellationTokenSource _samplerCancellation = new();
    private Task _writerTask = Task.CompletedTask;
    private Task _samplerTask = Task.CompletedTask;
    private (string? Name, string? Version, string? Protocol)? _client;
    private string? _workspaceKey;
    private long _sequence;
    private long _callSequence;
    private int _activeCalls;
    private long _lastActivity;
    private long? _lastLoad;
    private string _exitReason = "EOF stdin";
    private int _started;
    private int _stopped;
}
