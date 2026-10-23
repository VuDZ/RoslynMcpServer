using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Navigation;
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
    public async Task Clearing_the_workspace_ends_the_prewarm_without_an_exception()
    {
        await using var project = await TempProject.CreateAsync();
        var manager = SolutionManagerTestFactory.Create();
        manager.SearchPrewarmEnabled = true;
        await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        var prewarm = manager.SearchPrewarmTask;
        Assert.NotNull(prewarm);

        await manager.ClearWorkspaceAsync();

        // Cancelled or already finished: either way the task completes and never faults.
        await prewarm.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(prewarm.IsCompletedSuccessfully);
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
