using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Navigation;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Navigation;

public sealed class SearchIndexPrewarmTests
{
    [Fact]
    public async Task Prewarm_visits_every_project_and_logs_success()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(AddProject(workspace.CurrentSolution, "First"), "Second");
        var logger = new RecordingLogger();

        await SearchIndexPrewarm.RunAsync(solution, logger, CancellationToken.None);

        using var line = Parse(logger.Messages.Single());
        Assert.Equal("success", line.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(2, line.RootElement.GetProperty("projects").GetInt32());
        Assert.Equal(2, line.RootElement.GetProperty("completedProjects").GetInt32());
    }

    [Fact]
    public async Task Cancelled_prewarm_logs_cancellation_instead_of_throwing()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "First");
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await SearchIndexPrewarm.RunAsync(solution, logger, cancellation.Token);

        using var line = Parse(logger.Messages.Single());
        Assert.Equal("cancelled", line.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, line.RootElement.GetProperty("completedProjects").GetInt32());
    }

    [Fact]
    public void Without_the_environment_variable_the_file_setting_decides_and_the_default_is_on()
    {
        // The variable is process-wide; a test run that sets it checks the file-setting path elsewhere.
        if (Environment.GetEnvironmentVariable(SearchIndexPrewarm.EnvironmentVariableName) is not null)
        {
            return;
        }

        Assert.True(SearchIndexPrewarm.IsEnabled(fileSetting: null));
        Assert.True(SearchIndexPrewarm.IsEnabled(fileSetting: true));
        Assert.False(SearchIndexPrewarm.IsEnabled(fileSetting: false));
    }

    [Theory]
    [InlineData("0", true, false)]
    [InlineData(" false ", true, false)]
    [InlineData("FALSE", null, false)]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData(" ", false, false)]
    [InlineData(null, false, false)]
    public void The_environment_value_wins_over_the_file_setting(string? environmentValue, bool? fileSetting, bool expected)
    {
        Assert.Equal(expected, SearchIndexPrewarm.IsEnabled(fileSetting, environmentValue));
    }

    [Fact]
    public async Task Prewarm_ignores_a_cancellation_that_is_not_its_own()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "First");
        var logger = new RecordingLogger();

        await SearchIndexPrewarm.RunAsync(
            solution,
            logger,
            _ => throw new OperationCanceledException("Cancelled by another token."),
            CancellationToken.None);

        using var line = Parse(logger.Messages.Single());
        Assert.Equal("failure", line.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(typeof(OperationCanceledException).FullName, line.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Fresh_load_prewarms_and_a_later_search_finds_the_same_declarations()
    {
        await using var project = await TempProject.CreateAsync();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            manager.SearchPrewarmEnabled = true;
            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            Assert.NotNull(manager.SearchPrewarmTask);
            await manager.SearchPrewarmTask;

            var line = logger.Messages.Single(message => message.StartsWith(Prefix, StringComparison.Ordinal));
            using var json = Parse(line);
            Assert.Equal("success", json.RootElement.GetProperty("outcome").GetString());
            var solution = await manager.GetPublishedSolutionAsync();
            Assert.NotNull(solution);
            var found = await SymbolDeclarationResolver.ResolveDeclarationsAsync(solution, "Widget", SymbolFilter.Type);
            Assert.Single(found.Symbols);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    [Fact]
    public async Task Disabled_prewarm_does_not_start()
    {
        await using var project = await TempProject.CreateAsync();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            manager.SearchPrewarmEnabled = false;
            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

            Assert.Null(manager.SearchPrewarmTask);
            Assert.DoesNotContain(logger.Messages, message => message.StartsWith(Prefix, StringComparison.Ordinal));
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    [Fact]
    public async Task Clearing_the_workspace_cancels_a_running_prewarm_and_logs_it_before_the_clear()
    {
        await using var project = await TempProject.CreateAsync();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        manager.SearchPrewarmEnabled = true;
        var held = HoldFirstPrewarmProject(manager);
        await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        await held.WaitAsync(TimeSpan.FromSeconds(30));
        var prewarm = manager.SearchPrewarmTask;
        Assert.NotNull(prewarm);

        // The search does not wait for a prewarm that is stuck before its first project.
        var solution = await manager.GetPublishedSolutionAsync();
        Assert.NotNull(solution);
        var found = await SymbolDeclarationResolver.ResolveDeclarationsAsync(solution, "Widget", SymbolFilter.Type);
        Assert.Single(found.Symbols);

        await manager.ClearWorkspaceAsync();

        Assert.True(prewarm.IsCompletedSuccessfully);
        var messages = logger.Messages.ToList();
        var line = Assert.Single(messages, message => message.StartsWith(Prefix, StringComparison.Ordinal));
        using var json = Parse(line);
        Assert.Equal("cancelled", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("completedProjects").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("projects").GetInt32());
        Assert.True(
            messages.IndexOf(line) < messages.FindIndex(message => message.StartsWith("Roslyn workspace cleared", StringComparison.Ordinal)),
            "The prewarm of the cleared load must log before the clear.");
    }

    [Fact]
    public async Task Loading_another_solution_cancels_the_previous_prewarm_before_the_new_load()
    {
        await using var first = await TempProject.CreateAsync();
        await using var second = await TempProject.CreateAsync();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            manager.SearchPrewarmEnabled = true;
            var held = HoldFirstPrewarmProject(manager);
            await manager.LoadAndPrepareAsync(first.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            await held.WaitAsync(TimeSpan.FromSeconds(30));
            var firstPrewarm = manager.SearchPrewarmTask;

            await manager.LoadAndPrepareAsync(second.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            Assert.NotNull(firstPrewarm);
            Assert.True(firstPrewarm.IsCompletedSuccessfully);
            Assert.NotSame(firstPrewarm, manager.SearchPrewarmTask);
            await manager.SearchPrewarmTask!;

            var messages = logger.Messages.ToList();
            var lines = messages.Where(message => message.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            Assert.Equal(2, lines.Count);
            using var cancelled = Parse(lines[0]);
            using var finished = Parse(lines[1]);
            Assert.Equal("cancelled", cancelled.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("success", finished.RootElement.GetProperty("outcome").GetString());
            var secondLoaded = messages.FindIndex(message =>
                message.StartsWith("Loaded Roslyn workspace from " + second.ProjectPath, StringComparison.Ordinal));
            Assert.True(
                messages.IndexOf(lines[0]) < secondLoaded,
                "The prewarm of the replaced load must log before the next load completes.");
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    [Fact]
    public async Task A_cache_hit_keeps_the_prewarm_of_the_load_it_reuses()
    {
        await using var project = await TempProject.CreateAsync();
        var logger = new WorkspaceRecordingLogger();
        var manager = SolutionManagerTestFactory.Create(logger: logger);
        try
        {
            manager.SearchPrewarmEnabled = true;
            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            var prewarm = manager.SearchPrewarmTask;
            Assert.NotNull(prewarm);
            await prewarm;

            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

            Assert.True(manager.LastLoadWasCacheHit);
            Assert.Same(prewarm, manager.SearchPrewarmTask);
            Assert.Single(logger.Messages, message => message.StartsWith(Prefix, StringComparison.Ordinal));
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    [Fact]
    public async Task An_opt_in_analyzer_load_does_not_start_the_prewarm()
    {
        await using var project = await TempProject.CreateAsync();
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            manager.SearchPrewarmEnabled = true;
            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: true, CancellationToken.None);

            Assert.Null(manager.SearchPrewarmTask);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    /// <summary>Holds the first project of the first prewarm until it is cancelled; later prewarms run freely.</summary>
    private static Task HoldFirstPrewarmProject(SolutionManager manager)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        manager.BeforeSearchPrewarmProjectForTests = async cancellationToken =>
        {
            if (Interlocked.Increment(ref calls) != 1)
            {
                return;
            }

            held.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Roslyn notices the cancellation at its next document, not inside Cancel(). Without this
                // delay the whole prewarm would finish synchronously in Cancel() and hide a missing wait.
                await Task.Delay(200, CancellationToken.None);
                throw;
            }
        };
        return held.Task;
    }

    private static Solution AddProject(Solution solution, string name)
    {
        var projectId = ProjectId.CreateNewId();
        return solution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), name, name, LanguageNames.CSharp))
            .AddDocument(DocumentId.CreateNewId(projectId), "Widget.cs", "namespace " + name + "; public class Widget { }");
    }

    private static JsonDocument Parse(string message) => JsonDocument.Parse(message[Prefix.Length..]);

    private const string Prefix = "workspace_search_prewarm ";

    private sealed class TempProject : IAsyncDisposable
    {
        private TempProject(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public string ProjectPath => Path.Combine(Root, "App.csproj");

        public static async Task<TempProject> CreateAsync()
        {
            MsBuildBootstrapper.Register();
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpPrewarm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                encoding);
            await File.WriteAllTextAsync(Path.Combine(root, "Widget.cs"), "namespace App; public class Widget { }", encoding);
            return new TempProject(root);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A locked file only affects cleanup of a temporary directory.
            }

            return ValueTask.CompletedTask;
        }
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
