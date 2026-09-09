using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class TestDiscoveryHelperTests
{
    [Fact]
    public async Task ListTests_unfiltered_truncates_raw_list_before_later_projects()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 2,
            projectName: null,
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.FiltersApplied);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("projectFilter", out _));
        Assert.False(doc.RootElement.TryGetProperty("nameContains", out _));

        var methods = MethodNames(doc);
        Assert.Equal(new[] { "AlphaOne", "AlphaTwo" }, methods);
        Assert.All(
            doc.RootElement.GetProperty("tests").EnumerateArray(),
            t => Assert.Equal("App.Tests", t.GetProperty("projectName").GetString()));
    }

    [Fact]
    public async Task ListTests_projectName_scans_only_that_project_before_maxResults()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 3,
            projectName: "Other.Tests",
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.FiltersApplied);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("Other.Tests", doc.RootElement.GetProperty("projectFilter").GetString());
        Assert.Equal(new[] { "Unique", "Shared" }, MethodNames(doc));
        Assert.All(
            doc.RootElement.GetProperty("tests").EnumerateArray(),
            t => Assert.Equal("Other.Tests", t.GetProperty("projectName").GetString()));
    }

    [Fact]
    public async Task ListTests_nameContains_applies_before_maxResults()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 1,
            projectName: null,
            nameContains: "Unique",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.FiltersApplied);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Unique", doc.RootElement.GetProperty("nameContains").GetString());
        Assert.Equal(new[] { "Unique" }, MethodNames(doc));
    }

    [Fact]
    public async Task ListTests_nameContains_is_case_insensitive()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: null,
            nameContains: "betatests",
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(new[] { "Unique", "Shared" }, MethodNames(doc));
    }

    [Fact]
    public async Task ListTests_whitespace_filters_are_omitted()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: "  ",
            nameContains: "\t",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.FiltersApplied);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(5, doc.RootElement.GetProperty("count").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("projectFilter", out _));
        Assert.False(doc.RootElement.TryGetProperty("nameContains", out _));
    }

    [Fact]
    public async Task ListTests_unknown_project_returns_error_with_project_list()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: "Missing.Tests",
            nameContains: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("was not found", result.Payload, StringComparison.Ordinal);
        Assert.Contains("App.Tests", result.Payload, StringComparison.Ordinal);
        Assert.Contains("Other.Tests", result.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"count\"", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_ambiguous_project_returns_error()
    {
        using var ctx = AmbiguousAssemblyWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: "Shared.Tests",
            nameContains: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("matches 2 projects", result.Payload, StringComparison.Ordinal);
        Assert.Contains("Shared.Tests", result.Payload, StringComparison.Ordinal);
        Assert.Contains("Alias.Tests", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_matches_assembly_name()
    {
        using var ctx = AmbiguousAssemblyWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: "Alias.Tests",
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("AliasOne", MethodNames(doc).Single());
    }

    private static string?[] MethodNames(JsonDocument doc) =>
        doc.RootElement.GetProperty("tests").EnumerateArray()
            .Select(t => t.GetProperty("methodName").GetString())
            .ToArray();

    private sealed class TwoProjectWorkspace : IDisposable
    {
        private TwoProjectWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static TwoProjectWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "App.Tests",
                assemblyName: "App.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "App.Tests.csproj"),
                documentName: "AlphaTests.cs",
                source: """
                    namespace App.Tests;
                    public class AlphaTests
                    {
                        [Fact] public void AlphaOne() {}
                        [Fact] public void AlphaTwo() {}
                        [Fact] public void AlphaThree() {}
                    }
                    """);
            AddCSharpProject(
                workspace,
                name: "Other.Tests",
                assemblyName: "Other.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Other.Tests.csproj"),
                documentName: "BetaTests.cs",
                source: """
                    namespace Other.Tests;
                    public class BetaTests
                    {
                        [Fact] public void Unique() {}
                        [Fact] public void Shared() {}
                    }
                    """);
            return new TwoProjectWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    private sealed class AmbiguousAssemblyWorkspace : IDisposable
    {
        private AmbiguousAssemblyWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static AmbiguousAssemblyWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "Shared.Tests",
                assemblyName: "Shared.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Shared.Tests.csproj"),
                documentName: "SharedTests.cs",
                source: """
                    namespace Shared.Tests;
                    public class SharedTests
                    {
                        [Fact] public void SharedOne() {}
                    }
                    """);
            AddCSharpProject(
                workspace,
                name: "Alias.Tests",
                assemblyName: "Shared.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Alias.Tests.csproj"),
                documentName: "AliasTests.cs",
                source: """
                    namespace Alias.Tests;
                    public class AliasTests
                    {
                        [Fact] public void AliasOne() {}
                    }
                    """);
            return new AmbiguousAssemblyWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    private static void AddCSharpProject(
        AdhocWorkspace workspace,
        string name,
        string assemblyName,
        string csprojPath,
        string documentName,
        string source)
    {
        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            name,
            assemblyName,
            LanguageNames.CSharp,
            filePath: csprojPath);
        var project = workspace.AddProject(info);
        var filePath = Path.Combine(Path.GetDirectoryName(csprojPath) ?? Path.GetTempPath(), documentName);
        var document = project.AddDocument(documentName, SourceText.From(source), filePath: filePath);
        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
        }
    }
}
