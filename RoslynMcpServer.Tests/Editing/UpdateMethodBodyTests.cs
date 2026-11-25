using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Editing;

public sealed class UpdateMethodBodyTests
{
    [Theory]
    [InlineData("string")]
    [InlineData(null)]
    public Task A_supplied_signature_must_match_the_only_method(string? requestedType)
    {
        const string source = """
            class Target
            {
                int M(int value) => value;
            }
            """;

        return WithLoadedProjectAsync(source, async (tools, manager, path) =>
        {
            string[] signature = requestedType is null ? [] : [requestedType];
            var rejected = await tools.UpdateMethodBody(path, "Target", "M", "return 2;", signature);
            Assert.Contains("No overload matches parameterTypes", rejected, StringComparison.Ordinal);
            Assert.Contains("M(int)", rejected, StringComparison.Ordinal);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(source, await ReadSnapshotAsync(manager));

            var updated = await tools.UpdateMethodBody(path, "Target", "M", "return 2;");
            Assert.StartsWith("Updated body", updated, StringComparison.Ordinal);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("return 2;", saved, StringComparison.Ordinal);
            Assert.Equal(saved, await ReadSnapshotAsync(manager));
        });
    }

    [Fact]
    public Task An_empty_signature_selects_the_parameterless_overload()
    {
        const string source = """
            class Target
            {
                int M() => 1;
                int M(int value) => value;
            }
            """;

        return WithLoadedProjectAsync(source, async (tools, manager, path) =>
        {
            var ambiguous = await tools.UpdateMethodBody(path, "Target", "M", "return 2;");
            Assert.Contains("Specify parameterTypes to disambiguate overloads", ambiguous, StringComparison.Ordinal);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(source, await ReadSnapshotAsync(manager));

            var updated = await tools.UpdateMethodBody(path, "Target", "M", "return 2;", []);
            Assert.StartsWith("Updated body", updated, StringComparison.Ordinal);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("return 2;", saved, StringComparison.Ordinal);
            Assert.Contains("int M(int value) => value;", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("int M() => 1;", saved, StringComparison.Ordinal);
            Assert.Equal(saved, await ReadSnapshotAsync(manager));
        });
    }

    private static async Task WithLoadedProjectAsync(
        string source,
        Func<AstTools, SolutionManager, string, Task> runTest)
    {
        MsBuildBootstrapper.Register();
        var root = Directory.CreateTempSubdirectory("RoslynMcpMethodSignature-");
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            var projectPath = Path.Combine(root.FullName, "App.csproj");
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            var sourcePath = Path.Combine(root.FullName, "Target.cs");
            await File.WriteAllTextAsync(sourcePath, source);

            await manager.LoadAndPrepareAsync(
                projectPath,
                shadowCopyInSolutionAnalyzers: false,
                CancellationToken.None);
            await runTest(new AstTools(manager, NullLogger<AstTools>.Instance), manager, sourcePath);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            root.Delete(recursive: true);
        }
    }

    private static async Task<string> ReadSnapshotAsync(SolutionManager manager)
    {
        var snapshot = await manager.GetSanitizedPublishedSolutionAsync();
        Assert.NotNull(snapshot);
        var documents = snapshot.Projects.SelectMany(project => project.Documents);
        var document = Assert.Single(documents, document => document.Name == "Target.cs");
        return (await document.GetTextAsync()).ToString();
    }
}
