using ModelContextProtocol;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

/// <summary>
/// Forwards CLI step heartbeats to the MCP client as <c>notifications/progress</c>.
/// The SDK binds the tool's <see cref="IProgress{T}"/> parameter whether or not the client sent
/// a progress token: with a token it gets a real reporter, without one it gets an internal no-op
/// singleton. The no-op instance is treated as "no progress" so a token-less call never starts a
/// heartbeat at all (see <see cref="IsNoOpProgress"/>).
/// </summary>
internal sealed class McpToolProgressReporter : ICliProgressReporter
{
    /// <summary>
    /// Full name of the SDK's no-op progress singleton. It is <c>internal</c> in
    /// <c>ModelContextProtocol</c>, so the name is the only public discriminator; if a future SDK
    /// renames it the match is simply lost and the only cost is a harmless heartbeat timer.
    /// </summary>
    internal const string NoOpProgressTypeFullName = "ModelContextProtocol.NullProgress";

    private readonly IProgress<ProgressNotificationValue> _progress;
    private long _lastProgress;

    private McpToolProgressReporter(IProgress<ProgressNotificationValue> progress)
    {
        _progress = progress;
    }

    /// <summary>
    /// Wraps the SDK-supplied progress channel, or returns <see langword="null"/> when there is
    /// nothing to report to (no parameter or the SDK's no-op instance).
    /// </summary>
    public static ICliProgressReporter? TryCreate(IProgress<ProgressNotificationValue>? progress) =>
        progress is null || IsNoOpProgress(progress) ? null : new McpToolProgressReporter(progress);

    internal static bool IsNoOpProgress(IProgress<ProgressNotificationValue> progress) =>
        string.Equals(progress.GetType().FullName, NoOpProgressTypeFullName, StringComparison.Ordinal);

    public void Report(CliProgressUpdate update)
    {
        // ICliProgressReporter contract: progress must never change the CLI outcome, so a
        // disconnected transport / failed notification is swallowed here, not at every call site.
        try
        {
            // Numeric value is whole elapsed seconds, not a 1,2,3 counter that a naive bar renders
            // as "1%". No Total is sent, so it is not a completion fraction — hosts should render
            // Message. Still strictly increasing: a step boundary resets the per-step clock.
            var seconds = (long)Math.Max(0, update.Elapsed.TotalSeconds);
            _lastProgress = Math.Max(_lastProgress + 1, seconds);
            _progress.Report(new ProgressNotificationValue
            {
                Progress = _lastProgress,
                Message = update.Describe(),
            });
        }
        catch
        {
            // Best effort only.
        }
    }
}
