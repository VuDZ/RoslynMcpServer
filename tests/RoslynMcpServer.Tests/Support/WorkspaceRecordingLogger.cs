using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests.Support;

internal sealed class WorkspaceRecordingLogger : ILogger<SolutionManager>
{
    public IReadOnlyList<string> Messages => _messages.ToArray();

    public IReadOnlyList<Exception> Exceptions => _exceptions.ToArray();

    public event Action<string>? MessageLogged;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Enqueue(message);
        if (exception is not null)
        {
            _exceptions.Enqueue(exception);
        }
        MessageLogged?.Invoke(message);
    }

    private readonly ConcurrentQueue<string> _messages = new();
    private readonly ConcurrentQueue<Exception> _exceptions = new();
}
