using System.Diagnostics;
using System.Security;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

internal sealed class TestImpactWatcherFixture : IAsyncDisposable
{
    internal ProjectImpactFixture Files { get; } = new();
    internal SolutionManager Manager { get; } = SolutionManagerTestFactory.Create();
    internal string DeletedSourcePath => Path.Combine(Files.Root, "Library", "Old.cs");
    internal string ExcludedSourcePath => Path.Combine(Files.Root, "Specs", "Template.cs");
    internal Solution Solution => Manager.GetCurrentSolution()!;

    public async ValueTask DisposeAsync()
    {
        await Manager.ClearWorkspaceAsync();
        await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        Files.Dispose();
    }

    internal async Task LoadAsync()
    {
        MsBuildBootstrapper.Register();
        Files.ReplaceSource(Files.Library, DeletedSourcePath, "class Old { void Run() {} }");
        File.WriteAllText(ExcludedSourcePath, "class Template {}");
        Files.WriteBuildProjects();
        foreach (var project in Files.Solution.Projects)
        {
            var text = await File.ReadAllTextAsync(project.FilePath!);
            text = text.Replace("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                "<EnableDefaultCompileItems>true</EnableDefaultCompileItems>", StringComparison.Ordinal);
            foreach (var document in project.Documents.Where(document =>
                WorkspaceDiskPathFilter.IsPathUnderDirectory(document.FilePath!, Path.GetDirectoryName(project.FilePath!)!, InputPathCanon.Comparison)))
            {
                text = text.Replace("<Compile Include=\"" + SecurityElement.Escape(document.FilePath!) + "\" />", "", StringComparison.Ordinal);
            }
            if (project.Id == Files.Specs)
            {
                text = text.Replace("</Project>", "<ItemGroup><Compile Remove=\"Template.cs\" /></ItemGroup></Project>", StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(project.FilePath!, text);
        }
        await Manager.LoadAsync(Files.SolutionPath);
        await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(Manager.ProjectGraphStale);
    }

    internal Task<ProjectImpactResult> MapAsync(params string[] paths) =>
        ProjectImpactMapper.MapAsync(Solution, Manager.DiskWatcherSession!.InputMap!, paths,
            Files.SolutionPath, Manager.ProjectGraphStale, Manager.ProjectGraphStaleFromGraphFile, CancellationToken.None);

    internal async Task WaitForEventAsync(string path, int previousRevision = 0)
    {
        var stopwatch = Stopwatch.StartNew();
        while (Manager.DiskWatcherSession!.EventRevision(path) <= previousRevision && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(20);
        }
        Assert.True(Manager.DiskWatcherSession!.EventRevision(path) > previousRevision, "The native watcher did not observe " + path);
        // Event state is recorded immediately before the stale flag is raised in the same callback.
        while (!Manager.ProjectGraphStale && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(20);
        }
        Assert.True(Manager.ProjectGraphStale, "The native watcher did not mark the changed composition.");
    }

    internal string[] ProjectNames(IEnumerable<ProjectId> ids) =>
        ids.Select(id => Solution.GetProject(id)!.Name).Order(StringComparer.Ordinal).ToArray();
}
