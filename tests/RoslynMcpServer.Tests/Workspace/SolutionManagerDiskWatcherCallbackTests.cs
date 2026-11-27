using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class SolutionManagerDiskWatcherCallbackTests
{
    [Fact]
    public void Watcher_error_still_requests_full_refresh_when_the_logger_throws()
    {
        var logger = new ThrowingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        manager.StartDiskWatcherSessionForTests([]);

        var thrown = Record.Exception(() => manager.NotifyDiskWatcherError(new Win32Exception(5)));

        Assert.Null(thrown);
        Assert.True(manager.RefreshAllDocumentsPending);
        Assert.Equal(1, logger.Calls);
    }

    [Fact]
    public void Directory_rename_still_requests_full_refresh_when_the_logger_throws()
    {
        var logger = new ThrowingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        manager.StartDiskWatcherSessionForTests([]);

        var thrown = Record.Exception(() => manager.NotifyDiskWatcherDirectoryRename(Path.GetTempPath()));

        Assert.Null(thrown);
        Assert.True(manager.RefreshAllDocumentsPending);
        Assert.Equal(1, logger.Calls);
    }

    /// <summary>Same failure the Windows Event Log provider raises from a watcher callback.</summary>
    private sealed class ThrowingLogger : ILogger<SolutionManager>
    {
        public int Calls { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Calls++;
            throw new ObjectDisposedException("EventLogInternal");
        }
    }
}
