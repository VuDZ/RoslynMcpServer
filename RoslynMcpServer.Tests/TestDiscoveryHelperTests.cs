using System.Runtime.InteropServices;
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

    [Fact]
    public async Task ListTests_discovers_bound_xunit_attributes()
    {
        // Regression: GetSymbolInfo(AttributeSyntax).Symbol is the attribute constructor
        // (Name == ".ctor"), so the old code returned 0 tests for any workspace where the
        // attribute actually binds (every real MSBuildWorkspace load).
        using var ctx = TwoProjectWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 10,
            projectName: null,
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(5, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Contains("AlphaOne", MethodNames(doc));
        Assert.Contains("Unique", MethodNames(doc));
    }

    [Fact]
    public async Task ListTests_nameContains_finds_bound_class_by_name()
    {
        using var ctx = AttributeFormsWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 20,
            projectName: null,
            nameContains: "AllSoftOrderCompletedValidation",
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(4, doc.RootElement.GetProperty("count").GetInt32());
        Assert.All(
            doc.RootElement.GetProperty("tests").EnumerateArray(),
            t => Assert.Equal("AllSoftOrderCompletedValidationTests", t.GetProperty("className").GetString()));
    }

    [Fact]
    public async Task ListTests_does_not_match_lookalike_non_test_attributes()
    {
        // Guards against an over-broad matcher (it cannot detect a regression back to ".ctor" —
        // the positive tests above do that).
        using var ctx = AttributeFormsWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 20,
            projectName: null,
            nameContains: "NotATest",
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ListTests_discovers_custom_attribute_derived_from_fact()
    {
        // Mirrors RoslynMcpServer.Tests' own AnalyzerLifecycleFactAttribute : FactAttribute.
        using var ctx = DerivedAttributeWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 20,
            projectName: null,
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(new[] { "DerivedOne", "DerivedTwo" }, MethodNames(doc));
    }

    [Fact]
    public async Task ListTests_discovers_nunit_and_mstest_style_attribute_hierarchies()
    {
        using var ctx = ForeignFrameworkAttributeWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 20,
            projectName: null,
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        var expected = new[]
        {
            "NUnitTestCase", "NUnitTestCaseSource", "NUnitTest", "NUnitCustomDerived",
            "MSTestMethod", "MSTestDataMethod", "MSTestDerived"
        };
        Assert.Equal(
            expected.OrderBy(n => n, StringComparer.Ordinal),
            MethodNames(doc).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListTests_falls_back_to_syntax_name_when_attributes_do_not_bind()
    {
        // No metadata references at all: attribute types cannot resolve, so the syntactic
        // fallback must still report plain [Fact]/[Theory] methods (best-effort behaviour).
        using var ctx = UnboundAttributeWorkspace.Create();
        var result = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution,
            maxResults: 20,
            projectName: null,
            nameContains: null,
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Payload);
        Assert.Equal(new[] { "UnboundFact", "UnboundTheory", "UnboundQualifiedFact" }, MethodNames(doc));
    }

    [Fact]
    public async Task ListTests_reports_total_found_before_filters()
    {
        using var ctx = TwoProjectWorkspace.Create();
        var unfiltered = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution, 10, null, null, CancellationToken.None);
        var filtered = await TestDiscoveryHelper.ListTestsJsonAsync(
            ctx.Workspace.CurrentSolution, 10, null, "NoSuchTestName", CancellationToken.None);

        Assert.Equal(5, unfiltered.TotalTestMethodsFound);
        Assert.Equal(5, filtered.TotalTestMethodsFound);

        using var doc = JsonDocument.Parse(filtered.Payload);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("totalTestMethodsFound").GetInt32());
    }

    private static string[] MethodNames(JsonDocument doc) =>
        doc.RootElement.GetProperty("tests").EnumerateArray()
            .Select(t => t.GetProperty("methodName").GetString() ?? string.Empty)
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
                    using Xunit;
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
                    using Xunit;
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
                    using Xunit;
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
                    using Xunit;
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

    /// <summary>Attribute spellings (plain, suffixed, qualified) plus a look-alike non-test attribute.</summary>
    private sealed class AttributeFormsWorkspace : IDisposable
    {
        private AttributeFormsWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static AttributeFormsWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "Ucp.Tests",
                assemblyName: "Ucp.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Ucp.Tests.csproj"),
                documentName: "AllSoftOrderCompletedValidationTests.cs",
                source: """
                    using Xunit;
                    using System;

                    namespace Ucp.Tests;

                    public class AllSoftOrderCompletedValidationTests
                    {
                        [Fact] public void FactOne() {}
                        [FactAttribute] public void FactAttributeTwo() {}
                        [Xunit.Fact] public void QualifiedFact() {}
                        [Theory] public void TheoryOne() {}
                    }

                    public sealed class NotATestAttribute : Attribute {}

                    public class NotATestClass
                    {
                        [NotATest] public void NotATest() {}
                    }
                    """);
            return new AttributeFormsWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    /// <summary>Custom attribute deriving from xUnit's <c>FactAttribute</c> (like <c>AnalyzerLifecycleFactAttribute</c>).</summary>
    private sealed class DerivedAttributeWorkspace : IDisposable
    {
        private DerivedAttributeWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static DerivedAttributeWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "Analyzer.Lifecycle.Tests",
                assemblyName: "Analyzer.Lifecycle.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Analyzer.Lifecycle.Tests.csproj"),
                documentName: "LifecycleTests.cs",
                source: """
                    using Xunit;

                    namespace Analyzer.Lifecycle.Tests;

                    internal sealed class CustomFactAttribute : FactAttribute {}

                    public class LifecycleTests
                    {
                        [CustomFact] public void DerivedOne() {}
                        [CustomFact] public void DerivedTwo() {}
                    }
                    """);
            return new DerivedAttributeWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    /// <summary>
    /// NUnit/MSTest hierarchies declared in-source, so the matcher is proven framework-agnostic
    /// without adding those packages to this test project.
    /// </summary>
    private sealed class ForeignFrameworkAttributeWorkspace : IDisposable
    {
        private ForeignFrameworkAttributeWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static ForeignFrameworkAttributeWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "Foreign.Tests",
                assemblyName: "Foreign.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Foreign.Tests.csproj"),
                documentName: "ForeignTests.cs",
                source: """
                    using System;

                    namespace NUnit.Framework
                    {
                        public class TestAttribute : Attribute {}
                        public class TestCaseAttribute : Attribute {}
                        public class TestCaseSourceAttribute : Attribute {}
                    }

                    namespace Microsoft.VisualStudio.TestTools.UnitTesting
                    {
                        public class TestMethodAttribute : Attribute {}
                        public class DataTestMethodAttribute : TestMethodAttribute {}
                    }

                    namespace Foreign.Tests
                    {
                        using NUnit.Framework;
                        using Microsoft.VisualStudio.TestTools.UnitTesting;

                        public sealed class MyNUnitTestAttribute : TestAttribute {}

                        public class NUnitTests
                        {
                            [Test] public void NUnitTest() {}
                            [TestCase] public void NUnitTestCase() {}
                            [TestCaseSource] public void NUnitTestCaseSource() {}
                            [MyNUnitTest] public void NUnitCustomDerived() {}
                        }

                        public class MSTestTests
                        {
                            [TestMethod] public void MSTestMethod() {}
                            [DataTestMethod] public void MSTestDataMethod() {}
                        }

                        public sealed class DerivedTestMethodAttribute : TestMethodAttribute {}
                    }

                    namespace Foreign.Tests.More
                    {
                        using Microsoft.VisualStudio.TestTools.UnitTesting;

                        public class MSTestDerivedTests
                        {
                            [DerivedTestMethod] public void MSTestDerived() {}
                        }
                    }
                    """);
            return new ForeignFrameworkAttributeWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    /// <summary>No metadata references: attributes cannot bind, exercising the syntactic fallback.</summary>
    private sealed class UnboundAttributeWorkspace : IDisposable
    {
        private UnboundAttributeWorkspace(AdhocWorkspace workspace) => Workspace = workspace;

        public AdhocWorkspace Workspace { get; }

        public static UnboundAttributeWorkspace Create()
        {
            var workspace = new AdhocWorkspace();
            AddCSharpProject(
                workspace,
                name: "Unbound.Tests",
                assemblyName: "Unbound.Tests",
                csprojPath: Path.Combine(Path.GetTempPath(), "Unbound.Tests.csproj"),
                documentName: "UnboundTests.cs",
                source: """
                    namespace Unbound.Tests;

                    public class UnboundTests
                    {
                        [Fact] public void UnboundFact() {}
                        [Theory] public void UnboundTheory() {}
                        [Xunit.Fact] public void UnboundQualifiedFact() {}
                        [NotATest] public void Lookalike() {}
                    }
                    """,
                addMetadataReferences: false);
            return new UnboundAttributeWorkspace(workspace);
        }

        public void Dispose() => Workspace.Dispose();
    }

    private static void AddCSharpProject(
        AdhocWorkspace workspace,
        string name,
        string assemblyName,
        string csprojPath,
        string documentName,
        string source,
        bool addMetadataReferences = true)
    {
        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            name,
            assemblyName,
            LanguageNames.CSharp,
            filePath: csprojPath);
        var project = workspace.AddProject(info);
        if (addMetadataReferences)
        {
            // The attributes must actually bind, or HasTestAttribute silently falls back to the
            // syntactic name and this suite becomes a false green for the very bug it guards.
            // System.Runtime is required: without it FactAttribute's base `Attribute` is unresolved
            // (CS0012) and Roslyn returns no symbol for the attribute at all.
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            project = project
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));
        }

        var filePath = Path.Combine(Path.GetDirectoryName(csprojPath) ?? Path.GetTempPath(), documentName);
        var document = project.AddDocument(documentName, SourceText.From(source), filePath: filePath);
        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("TryApplyChanges failed for test workspace.");
        }
    }
}
