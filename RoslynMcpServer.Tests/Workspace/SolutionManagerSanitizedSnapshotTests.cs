using System.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class SolutionManagerSanitizedSnapshotTests
{
    [Fact]
    public async Task Clearing_the_workspace_drops_the_cached_sanitized_snapshot()
    {
        await using var project = await TempProject.CreateAsync();
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            await manager.LoadAndPrepareAsync(project.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            Assert.NotNull(await manager.GetSanitizedPublishedSolutionAsync());
            Assert.NotNull(manager.SanitizedSnapshotSource);

            await manager.ClearWorkspaceAsync();

            Assert.Null(manager.SanitizedSnapshotSource);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    [Fact]
    public async Task A_fresh_load_of_another_project_does_not_keep_the_previous_sanitized_snapshot()
    {
        await using var first = await TempProject.CreateAsync();
        await using var second = await TempProject.CreateAsync();
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            await manager.LoadAndPrepareAsync(first.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            Assert.NotNull(await manager.GetSanitizedPublishedSolutionAsync());
            var firstSource = manager.SanitizedSnapshotSource;
            Assert.NotNull(firstSource);

            await manager.LoadAndPrepareAsync(second.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

            Assert.NotSame(firstSource, manager.SanitizedSnapshotSource);
            var secondSnapshot = await manager.GetSanitizedPublishedSolutionAsync();
            Assert.NotNull(secondSnapshot);
            Assert.Contains(
                secondSnapshot.Projects,
                loaded => string.Equals(loaded.FilePath, second.ProjectPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

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
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcpSanitized-" + Guid.NewGuid().ToString("N"));
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
}
