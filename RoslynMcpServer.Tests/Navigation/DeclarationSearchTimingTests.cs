using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services.Navigation;
using Xunit;

namespace RoslynMcpServer.Tests.Navigation;

public sealed class DeclarationSearchTimingTests
{
    [Fact]
    public async Task Timings_cover_each_project_without_changing_declarations()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "First");
        solution = AddProject(solution, "Second");
        var baseline = await SymbolDeclarationResolver.ResolveDeclarationsAsync(solution, "Widget", SymbolFilter.Type);
        var logger = new RecordingLogger();
        var measured = await SymbolDeclarationResolver.ResolveDeclarationsAsync(
            solution, "Widget", SymbolFilter.Type, logger, new AdvancingTimeProvider());

        Assert.Null(measured.Error);
        Assert.Equal(2, measured.Symbols.Count);
        Assert.Equal(baseline.Symbols.Select(SymbolDeclarationResolver.GetSymbolFqn),
            measured.Symbols.Select(SymbolDeclarationResolver.GetSymbolFqn));
        Assert.Equal(3, logger.Messages.Count);
        var searchIds = new HashSet<string>();
        foreach (var message in logger.Messages.Take(2))
        {
            using var row = Parse(message, "workspace_search_project ");
            searchIds.Add(row.RootElement.GetProperty("searchId").GetString()!);
            Assert.Equal("success", row.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(10, row.RootElement.GetProperty("elapsedMs").GetDouble());
            Assert.Equal(1, row.RootElement.GetProperty("matches").GetInt32());
        }
        using var summary = Parse(logger.Messages[^1], "workspace_search_summary ");
        searchIds.Add(summary.RootElement.GetProperty("searchId").GetString()!);
        Assert.Single(searchIds);
        Assert.Equal(2, summary.RootElement.GetProperty("completedProjects").GetInt32());
        Assert.Equal(50, summary.RootElement.GetProperty("elapsedMs").GetDouble());
    }

    [Fact]
    public async Task Cancellation_is_rethrown_and_logged_for_the_reached_project()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "First");
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SymbolDeclarationResolver.ResolveDeclarationsAsync(
            solution, "Widget", SymbolFilter.Type, logger, new AdvancingTimeProvider(), cancellation.Token));

        Assert.Equal(2, logger.Messages.Count);
        using var project = Parse(logger.Messages[0], "workspace_search_project ");
        Assert.Equal("cancelled", project.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, project.RootElement.GetProperty("matches").ValueKind);
        using var summary = Parse(logger.Messages[1], "workspace_search_summary ");
        Assert.Equal("cancelled", summary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, summary.RootElement.GetProperty("completedProjects").GetInt32());
    }

    private static Solution AddProject(Solution solution, string name)
    {
        var projectId = ProjectId.CreateNewId();
        return solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), name + ".csproj")))
            .AddDocument(DocumentId.CreateNewId(projectId), "Widget.cs", SourceText.From($"namespace {name}; class Widget {{ }}"));
    }

    private static JsonDocument Parse(string message, string prefix)
    {
        Assert.StartsWith(prefix, message);
        return JsonDocument.Parse(message[prefix.Length..]);
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, 10);

        private long _timestamp;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
