using System.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace BuildHostFork.Trace;

/// <summary>
/// Records the loader's operations with their end on the Stopwatch clock.
/// </summary>
/// <remarks>
/// The 5.9.0 loader stops its own Stopwatch and calls <see cref="Report"/> synchronously right after the operation, so
/// the moment of the call is its end and the start is that moment minus the reported duration. <c>Progress&lt;T&gt;</c>
/// would post the call to a synchronization context and lose that moment.
/// </remarks>
internal sealed class ProgressRecorder : IProgress<ProjectLoadProgress>
{
    public IReadOnlyList<ClientOperation> Operations
    {
        get
        {
            lock (_operations)
            {
                return _operations.ToArray();
            }
        }
    }

    public void Report(ProjectLoadProgress value)
    {
        var end = Stopwatch.GetTimestamp();
        var duration = (long)(value.ElapsedTime.Ticks * (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        var operation = new ClientOperation(value.Operation.ToString(), value.FilePath, value.TargetFramework, end - duration, end);
        lock (_operations)
        {
            _operations.Add(operation);
        }
    }

    private readonly List<ClientOperation> _operations = [];
}
