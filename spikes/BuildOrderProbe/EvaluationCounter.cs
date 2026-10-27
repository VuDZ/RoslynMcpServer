using Microsoft.Build.Framework;

namespace BuildOrderProbe;

/// <summary>
/// Counts the evaluations the build manager runs. It is a logger of the build session only, like the BuildHost's
/// loggers, so the root evaluations of the project collection are not counted: the count is the evaluations that
/// design-time builds started for projects they reached through the MSBuild task.
/// </summary>
internal sealed class EvaluationCounter : ILogger
{
    public int Count => Volatile.Read(ref _count);

    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;

    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
        => eventSource.AnyEventRaised += (_, args) =>
        {
            if (args is ProjectEvaluationStartedEventArgs)
            {
                Interlocked.Increment(ref _count);
            }
        };

    public void Shutdown()
    {
    }

    private int _count;
}
