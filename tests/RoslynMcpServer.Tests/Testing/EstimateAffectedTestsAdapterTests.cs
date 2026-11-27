using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class EstimateAffectedTestsAdapterTests
{
    public static IEnumerable<object[]> AdapterCases() => VstestRunPlannerTests.AdapterProbes();

    [Theory]
    [MemberData(nameof(AdapterCases))]
    public async Task Actual_spike_source_edit_through_tool_selects_every_measured_runtime_case(string adapterName, string probe)
    {
        var root = RepositoryRoot.Find();
        var folder = adapterName.StartsWith("xunit", StringComparison.Ordinal) ? "Xunit"
            : adapterName == "nunit" ? "NUnit" : "MSTest";
        await using var fixture = new BaselineLifecycleFixture();
        var sourceDirectory = RepositoryRoot.Combine(root, "spikes", "AdapterTestNames", folder);
        var methodName = probe switch { "nested" => "Nested", "generic" => "Generic", "inherited" => "Inherited", _ => "Rows" };
        string? editedPath = null;
        foreach (var original in Directory.GetFiles(sourceDirectory, "*.cs"))
        {
            var destination = Path.Combine(fixture.Root, Path.GetFileName(original));
            File.Copy(original, destination);
            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow.AddDays(-1));
            var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(destination));
            if (tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == methodName))
            {
                editedPath = destination;
            }
        }
        Assert.NotNull(editedPath);
        await PrepareProjectAsync(fixture);
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        var source = await File.ReadAllTextAsync(editedPath);
        await fixture.WriteThroughMcpAsync(editedPath, ChangeMethod(source, methodName));
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests(includeDetails: true);
        EstimateAffectedTestsTests.AssertResponse(result);
        Assert.Contains("App.csproj", result, StringComparison.Ordinal);

        using var catalog = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine(root,
            "docs", "rapid-tia", "epoch-1-project-level", "evidence", "task-06-adapter-names", "catalog.json")));
        var adapter = catalog.RootElement.GetProperty("Adapters").EnumerateArray()
            .Single(item => item.GetProperty("Adapter").GetString() == adapterName);
        var expected = adapter.GetProperty("Filters").EnumerateArray()
            .Single(item => item.GetProperty("Probe").GetString() == probe)
            .GetProperty("Results").EnumerateArray().Select(item => item.GetProperty("TestCaseId").GetString()).ToHashSet();
        var filter = Filter(result);
        var selected = adapter.GetProperty("Cases").EnumerateArray()
            .Where(item => filter is null || Matches(filter, item.GetProperty("FullyQualifiedName").GetString()!))
            .Select(item => item.GetProperty("Id").GetString()).ToHashSet();
        Assert.NotEmpty(expected);
        Assert.True(expected.IsSubsetOf(selected), "The public tool plan omitted measured adapter tests: " + result);
        if (probe is "nested" or "parameters" || adapterName == "nunit" && probe == "generic")
        {
            Assert.NotNull(filter);
            Assert.Equal(expected.Order(), selected.Order());
        }
    }

    [Theory]
    [InlineData("[NUnit.Framework.TestFixture(\"a\")]", "Fixture")]
    [InlineData("[NUnit.Framework.TestFixtureSource(\"Cases\")]", "Fixture")]
    [InlineData("[NUnit.Framework.TestFixture(typeof(int), 1)]", "Fixture<T>")]
    public async Task NUnit_constructor_and_source_arguments_use_whole_project_through_tool(string attribute, string className)
    {
        await using var fixture = new BaselineLifecycleFixture();
        await PrepareProjectAsync(fixture);
        var path = Path.Combine(fixture.Root, "Fixture.cs");
        var source = "namespace Ns { " + attribute + " public class " + className
            + " { [NUnit.Framework.Test] public void Check() {} } }";
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        await fixture.WriteThroughMcpAsync(path, ChangeMethod(source, "Check"));
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(result);
        Assert.Null(Filter(result));
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture constructor/source arguments", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Constructor")]
    [InlineData("Source")]
    public async Task Inherited_NUnit_fixture_constructor_and_source_arguments_use_whole_project_through_tool(string method)
    {
        await using var fixture = new BaselineLifecycleFixture();
        await PrepareProjectAsync(fixture);
        var path = Path.Combine(fixture.Root, "InheritedFixture.cs");
        const string source = """
            namespace Ns {
                [NUnit.Framework.TestFixture(42)]
                public abstract class ArgumentBase { protected ArgumentBase(int value) {} }
                public class ArgumentDerived : ArgumentBase {
                    public ArgumentDerived(int value) : base(value) {}
                    [NUnit.Framework.Test] public void Constructor() {}
                }
                [NUnit.Framework.TestFixtureSource(nameof(Cases))]
                public abstract class SourceBase {
                    public static object[] Cases => new object[] { new object[] { 42 } };
                    protected SourceBase(int value) {}
                }
                public class SourceDerived : SourceBase {
                    public SourceDerived(int value) : base(value) {}
                    [NUnit.Framework.Test] public void Source() {}
                }
            }
            """;
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        await fixture.WriteThroughMcpAsync(path, ChangeMethod(source, method));
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        EstimateAffectedTestsTests.AssertResponse(result);
        Assert.Null(Filter(result));
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture constructor/source arguments", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unmeasured_generic_identity_is_explicit_whole_project_fallback_through_tool()
    {
        await using var fixture = new BaselineLifecycleFixture();
        await PrepareProjectAsync(fixture);
        var path = Path.Combine(fixture.Root, "Open.cs");
        const string source = "namespace Ns { public class Open<T> { [Xunit.Fact] public void Check() {} } }";
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        await fixture.LoadAsync();
        await fixture.Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        await fixture.WriteThroughMcpAsync(path, ChangeMethod(source, "Check"));
        var result = await EstimateAffectedTestsTests.Tools(fixture.Manager).EstimateAffectedTests();

        Assert.Null(Filter(result));
        Assert.Contains("runtime identity", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whole project", result, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PrepareProjectAsync(BaselineLifecycleFixture fixture)
    {
        var project = await File.ReadAllTextAsync(fixture.ProjectPath);
        project = project.Replace("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
            "<EnableDefaultCompileItems>true</EnableDefaultCompileItems>", StringComparison.Ordinal)
            .Replace("<Compile Include=\"A.cs\" /><Compile Include=\"B.cs\" />", "", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.ProjectPath, project);
        File.WriteAllText(fixture.FirstPath, FrameworkStubs);
        File.WriteAllText(fixture.SecondPath, "");
        File.SetLastWriteTimeUtc(fixture.FirstPath, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(fixture.SecondPath, DateTime.UtcNow.AddDays(-1));
    }

    private static string ChangeMethod(string source, string name)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
        var statements = method.Body?.Statements ?? SyntaxFactory.List<StatementSyntax>(
            [SyntaxFactory.ExpressionStatement(method.ExpressionBody!.Expression)]);
        var changed = method.WithExpressionBody(null).WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(statements.Insert(0, SyntaxFactory.ParseStatement("System.GC.KeepAlive(42);"))));
        return root.ReplaceNode(method, changed).NormalizeWhitespace().ToFullString();
    }

    private static string? Filter(string response)
    {
        var line = Assert.Single(response.Split(Environment.NewLine), line => line.StartsWith("Run: ", StringComparison.Ordinal));
        const string marker = " with filter `";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? null : line[(index + marker.Length)..line.LastIndexOf('`')];
    }

    private static bool Matches(string filter, string name) => filter.Split('|').Any(term =>
        name.Contains(term["FullyQualifiedName~".Length..], StringComparison.Ordinal));

    private const string FrameworkStubs = """
        namespace Xunit {
            public class FactAttribute : System.Attribute {}
            public class TheoryAttribute : System.Attribute {}
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public class InlineDataAttribute : System.Attribute { public InlineDataAttribute(params object[] values) {} }
            public static class Assert {
                public static void True(bool value) {}
                public static void Equal(object first, object second) {}
                public static void InRange(int value, int first, int second) {}
            }
        }
        namespace NUnit.Framework {
            public class TestAttribute : System.Attribute {}
            public class TestFixtureAttribute : System.Attribute { public TestFixtureAttribute(params object[] values) {} }
            public class TestFixtureSourceAttribute : System.Attribute { public TestFixtureSourceAttribute(string name) {} }
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public class TestCaseAttribute : System.Attribute { public TestCaseAttribute(params object[] values) {} }
            public static class Assert { public static void That(object value, object constraint) {} }
            public static class Is {
                public static object True => new object();
                public static object EqualTo(object value) => value;
                public static object InRange(int first, int second) => first;
            }
        }
        namespace Microsoft.VisualStudio.TestTools.UnitTesting {
            public class TestClassAttribute : System.Attribute {}
            public class TestMethodAttribute : System.Attribute {}
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public class DataRowAttribute : System.Attribute { public DataRowAttribute(params object[] values) {} }
            public static class Assert {
                public static void IsTrue(bool value) {}
                public static void AreEqual(object first, object second) {}
            }
        }
        """;
}
