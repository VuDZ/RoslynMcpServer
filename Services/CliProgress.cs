namespace RoslynMcpServer.Services;

/// <summary>
/// One coarse progress observation for a long-running CLI step.
/// Deliberately carries no stdout and no machine-specific paths: protocol progress is a
/// heartbeat for a live agent, not a log channel.
/// </summary>
/// <param name="Stage">Stable step label, e.g. <c>dotnet build -v:minimal</c>.</param>
/// <param name="Elapsed">
/// Wall-clock time since <b>this step</b> started, never since the probe started:
/// <see cref="TimeSpan.Zero"/> on a step boundary, then growing with heartbeats.
/// </param>
/// <param name="LastExitCode">Exit code of the previously completed step, when one already finished.</param>
public readonly record struct CliProgressUpdate(string Stage, TimeSpan Elapsed, int? LastExitCode)
{
    /// <summary>Short protocol text: step, elapsed, previous exit. No stdout, no file paths.</summary>
    public string Describe()
    {
        var text = Elapsed > TimeSpan.Zero
            ? $"{Stage}: still running ({Elapsed.TotalSeconds:0}s elapsed)"
            : $"{Stage}: starting";
        return LastExitCode is { } exitCode ? $"{text}; previous step exit {exitCode}" : text;
    }
}

/// <summary>
/// Reports coarse progress of a long-running dotnet CLI step to the MCP client.
/// Implementations must be non-blocking and must never throw: progress outages (client
/// disconnect, host without a token) must not change the CLI outcome.
/// </summary>
public interface ICliProgressReporter
{
    /// <summary>Reports that the current step is still running.</summary>
    void Report(CliProgressUpdate update);
}

/// <summary>
/// Optional progress wiring for a single <see cref="DotNetCliRunner"/> invocation:
/// where heartbeats go and which step label they carry.
/// </summary>
public sealed record CliProgressWatch(ICliProgressReporter Reporter, string Stage)
{
    /// <summary>Default heartbeat cadence: live enough for an agent, off the CLI hot path.</summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>Heartbeat cadence. Shortened in tests to observe an artificially slow step.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = DefaultHeartbeatInterval;

    /// <summary>Exit code of the last completed step, when one already finished.</summary>
    public int? LastExitCode { get; init; }
}
