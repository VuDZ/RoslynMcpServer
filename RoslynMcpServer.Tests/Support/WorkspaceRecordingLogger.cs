using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests.Support;

internal sealed class WorkspaceRecordingLogger : ILogger<SolutionManager>
{
    public IReadOnlyList<string> Messages => _messages.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _messages.Enqueue(formatter(state, exception));
    }

    private readonly ConcurrentQueue<string> _messages = new();
}
