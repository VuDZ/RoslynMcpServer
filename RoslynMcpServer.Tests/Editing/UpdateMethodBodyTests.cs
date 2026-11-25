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

    [Fact]
    public Task Wrong_parameter_type_case_rejects_the_only_method()
    {
        const string source = """
            namespace N;
            class Order { }
            class ORDER { }
            class Target
            {
                int M(Order value) => 1;
            }
            """;

        return WithLoadedProjectAsync(source, async (tools, manager, path) =>
        {
            var rejected = await tools.UpdateMethodBody(path, "Target", "M", "return 2;", ["ORDER"]);
            Assert.Contains("No overload matches parameterTypes", rejected, StringComparison.Ordinal);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(source, await ReadSnapshotAsync(manager));
        });
    }

    [Theory]
    [InlineData("Order", "int M(ORDER value) => 2;")]
    [InlineData("ORDER", "int M(Order value) => 1;")]
    [InlineData("global::N.Order", "int M(ORDER value) => 2;")]
    [InlineData("global::N.ORDER", "int M(Order value) => 1;")]
    public Task Parameter_type_case_selects_the_requested_overload(string requestedType, string untouchedMethod)
    {
        const string source = """
            namespace N;
            class Order { }
            class ORDER { }
            class Target
            {
                int M(Order value) => 1;
                int M(ORDER value) => 2;
            }
            """;

        return WithLoadedProjectAsync(source, async (tools, manager, path) =>
        {
            var updated = await tools.UpdateMethodBody(path, "Target", "M", "return 3;", [requestedType]);
            Assert.StartsWith("Updated body", updated, StringComparison.Ordinal);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("return 3;", saved, StringComparison.Ordinal);
            Assert.Contains(untouchedMethod, saved, StringComparison.Ordinal);
            Assert.Equal(saved, await ReadSnapshotAsync(manager));
        });
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("Order<string>")]
    public Task A_generic_parameter_requires_matching_type_arguments(string requestedType)
    {
        const string Source = """
            class Order { }
            class Order<T> { }
            class Target
            {
                int Generic(Order<int> value) => 1;
            }
            """;

        return WithLoadedProjectAsync(Source, async (tools, manager, path) =>
        {
            var rejected = await tools.UpdateMethodBody(path, "Target", "Generic", "return 3;", [requestedType]);
            Assert.Contains("No overload matches parameterTypes", rejected, StringComparison.Ordinal);
            Assert.Equal(Source, await File.ReadAllTextAsync(path));
            Assert.Equal(Source, await ReadSnapshotAsync(manager));
        });
    }

    [Theory]
    [InlineData("Order", "int Generic(N.Order<int> value) => 2;")]
    [InlineData("Order<int>", "int Generic(N.Order value) => 1;")]
    [InlineData("global::N.Order<int>", "int Generic(N.Order value) => 1;")]
    public Task Type_arguments_select_the_requested_overload(string requestedType, string untouchedMethod)
    {
        const string Source = """
            namespace N;
            class Order { }
            class Order<T> { }
            class Target
            {
                int Generic(N.Order value) => 1;
                int Generic(N.Order<int> value) => 2;
            }
            """;

        return WithLoadedProjectAsync(Source, async (tools, manager, path) =>
        {
            var updated = await tools.UpdateMethodBody(path, "Target", "Generic", "return 3;", [requestedType]);
            Assert.StartsWith("Updated body", updated, StringComparison.Ordinal);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("return 3;", saved, StringComparison.Ordinal);
            Assert.Contains(untouchedMethod, saved, StringComparison.Ordinal);
            Assert.Equal(saved, await ReadSnapshotAsync(manager));
        });
    }

    [Fact]
    public Task A_selector_must_resolve_to_the_type_of_the_only_candidate()
    {
        const string Source = """
            namespace A { class Order { } class Int32 { } }
            namespace B { class Int32 { } }
            namespace N
            {
                using A;
                using B;
                class Order { }
                class Box<T> { }
                class Node { }
                class Outer<T> { public class Node { } }
                class Target
                {
                    int Mixed(Box<A.Order> value) => 1;
                    int Nested(Outer<int>.Node value) => 1;
                    int Number(int value) => 1;
                }
            }
            """;

        return WithLoadedProjectAsync(Source, async (tools, manager, path) =>
        {
            (string Method, string Selector)[] requests = [("Mixed", "Box<Order>"), ("Nested", "Node"), ("Number", "Int32")];
            foreach (var (method, selector) in requests)
            {
                var rejected = await tools.UpdateMethodBody(path, "Target", method, "return 3;", [selector]);
                Assert.Contains("No overload matches parameterTypes", rejected, StringComparison.Ordinal);
                Assert.Equal(Source, await File.ReadAllTextAsync(path));
                Assert.Equal(Source, await ReadSnapshotAsync(manager));
            }
        });
    }

    [Fact]
    public Task An_unresolved_generic_argument_cannot_match_its_own_spelling()
    {
        const string Source = """
            class Box<T> { }
            class Target
            {
                int Mixed(Box<Missing> value) => 1;
            }
            """;

        return WithLoadedProjectAsync(Source, async (tools, manager, path) =>
        {
            var rejected = await tools.UpdateMethodBody(path, "Target", "Mixed", "return 3;", ["Box<Missing>"]);
            Assert.Contains("No overload matches parameterTypes", rejected, StringComparison.Ordinal);
            Assert.Equal(Source, await File.ReadAllTextAsync(path));
            Assert.Equal(Source, await ReadSnapshotAsync(manager));
        });
    }

    [Theory]
    [InlineData("Mixed", "Box<Order>", "int Mixed(Box<A.Order> value) => 2;")]
    [InlineData("Mixed", "Box<N.Order>", "int Mixed(Box<A.Order> value) => 2;")]
    [InlineData("Mixed", "Box<A.Order>", "int Mixed(Box<N.Order> value) => 1;")]
    [InlineData("Nested", "Node", "int Nested(Outer<int>.Node value) => 2;")]
    [InlineData("Nested", "Outer<int>.Node", "int Nested(N.Node value) => 1;")]
    [InlineData("Number", "Int32", "int Number(string value) => 2;")]
    public Task A_bound_selector_selects_only_the_matching_overload(string method, string selector, string untouchedMethod)
    {
        const string Source = """
            namespace A { class Order { } }
            namespace N
            {
                class Order { }
                class Box<T> { }
                class Node { }
                class Outer<T> { public class Node { } }
                class Target
                {
                    int Mixed(Box<N.Order> value) => 1;
                    int Mixed(Box<A.Order> value) => 2;
                    int Nested(N.Node value) => 1;
                    int Nested(Outer<int>.Node value) => 2;
                    int Number(int value) => 1;
                    int Number(string value) => 2;
                }
            }
            """;

        return WithLoadedProjectAsync(Source, async (tools, manager, path) =>
        {
            var updated = await tools.UpdateMethodBody(path, "Target", method, "return 3;", [selector]);
            Assert.StartsWith("Updated body", updated, StringComparison.Ordinal);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("return 3;", saved, StringComparison.Ordinal);
            Assert.Contains(untouchedMethod, saved, StringComparison.Ordinal);
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
            // Snapshot assertions should observe the editor's publication without a disk watcher reloading the file.
            manager.SilenceDiskWatchersForTests();
            await runTest(new AstTools(manager, NullLogger<AstTools>.Instance), manager, sourcePath);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
            await DeleteTemporaryProjectAsync(root);
        }
    }

    private static async Task DeleteTemporaryProjectAsync(DirectoryInfo root)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                root.Delete(recursive: true);
                return;
            }
            catch (IOException exception) when (OperatingSystem.IsWindows()
                && (exception.HResult & 0xffff) is 32 or 33 && attempt < 20)
            {
                // A backup left by ReplaceFileW can still have a transient sharing lock after the workspace closes.
                await Task.Delay(100);
            }
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
