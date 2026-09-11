using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

[Collection("AnalyzerLoaderContract")]
public sealed class AnalyzerLoaderContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "RoslynMcpServer.Tests",
        "Epoch3",
        Guid.NewGuid().ToString("N"));

    public AnalyzerLoaderContractTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Host_contract_catalog_is_exact_names_not_a_system_prefix()
    {
        Assert.Contains("Microsoft.CodeAnalysis", AnalyzerHostContractCatalog.SharedHostContractSimpleNames);
        Assert.Contains("Microsoft.CodeAnalysis.CSharp", AnalyzerHostContractCatalog.SharedHostContractSimpleNames);
        Assert.Contains("netstandard", AnalyzerHostContractCatalog.SharedHostContractSimpleNames);
        Assert.False(AnalyzerHostContractCatalog.IsSharedHostContract("System.Xml"));
        Assert.False(AnalyzerHostContractCatalog.IsSharedHostContract("Generator.Helpers"));
        Assert.False(AnalyzerHostContractCatalog.IsSharedHostContract("Roslyn"));
        Assert.True(AnalyzerHostContractCatalog.IsSharedHostContract(typeof(Compilation).Assembly.GetName()));
        var forged = new AssemblyName("Microsoft.CodeAnalysis");
        forged.SetPublicKeyToken([0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]);
        Assert.False(AnalyzerHostContractCatalog.IsSharedHostContract(forged));
    }

    [Fact]
    public void Inspector_treats_host_contracts_as_supported_and_helper_as_private()
    {
        var main = Emit(
            "InspectMain",
            """
            public static class MainType { public static int X => 1; }
            """,
            extraReferences: Array.Empty<MetadataReference>());
        var inspection = AnalyzerPrivateDependencyInspector.Inspect(main);
        Assert.Null(inspection.FailureReason);
        Assert.NotNull(inspection.Identity);
        Assert.DoesNotContain(
            inspection.PrivateReferences,
            r => string.Equals(r.Name, "netstandard", StringComparison.OrdinalIgnoreCase)
                || AnalyzerHostContractCatalog.IsSharedHostContract(r));

        var helper = Emit(
            "Inspect.Helpers",
            """
            namespace Generator.Helpers;
            public static class HelperInfo { public static string Name { get; } = "H"; }
            """);
        var withHelper = Emit(
            "InspectWithHelper",
            """
            public static class UsesHelper
            {
                public static string Name => Generator.Helpers.HelperInfo.Name;
            }
            """,
            extraReferences: new[] { MetadataReference.CreateFromFile(helper) });
        var helperInspection = AnalyzerPrivateDependencyInspector.Inspect(withHelper);
        Assert.Contains(
            helperInspection.PrivateReferences,
            r => string.Equals(r.Name, "Inspect.Helpers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gate_refuses_private_helper_and_same_identity_collision()
    {
        var helper = Emit(
            "Gate.Helpers",
            "namespace Generator.Helpers; public static class HelperInfo { public static string Name { get; } = \"H\"; }");
        var withHelper = Emit(
            "GateWithHelper",
            "public static class UsesHelper { public static string Name => Generator.Helpers.HelperInfo.Name; }",
            extraReferences: new[] { MetadataReference.CreateFromFile(helper) });
        var inspect = AnalyzerPrivateDependencyInspector.Inspect(withHelper);
        Assert.True(
            inspect.PrivateReferences.Any(r => string.Equals(r.Name, "Gate.Helpers", StringComparison.OrdinalIgnoreCase)),
            "refs=" + string.Join(",", inspect.References.Select(r => r.Name)) + " fail=" + inspect.FailureReason);
        var loader = new InProcessAnalyzerAssemblyLoader();
        var dependency = AnalyzerExecutionGate.EvaluateAssemblyPath(
            withHelper,
            "Consumer",
            "GateWithHelper",
            "gen-1",
            loader);
        Assert.Equal(AnalyzerExecutionStatus.DependencyUnsupported, dependency.Status);
        Assert.Equal("Gate.Helpers", dependency.DependencyName);
        Assert.Equal("Consumer", dependency.ProjectName);

        var first = Emit("CollideGen", "public static class G { public static int V => 1; }");
        var second = Emit("CollideGen", "public static class G { public static int V => 2; }");
        Assert.NotEqual(first, second);
        _ = loader.LoadFromPath(first);
        var collision = AnalyzerExecutionGate.EvaluateAssemblyPath(second, "Consumer", "CollideGen", "gen-2", loader);
        Assert.False(collision.PermitsExecution);
        Assert.True(collision.RequiresRestart);
        Assert.Contains("restart", collision.Action ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Loader_does_not_first_match_simple_name_and_records_resolve()
    {
        var helperA = Path.Combine(_root, "a", "Generator.Helpers.dll");
        var helperB = Path.Combine(_root, "b", "Generator.Helpers.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(helperA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(helperB)!);
        File.Copy(
            Emit("Resolve.Helpers", "namespace Generator.Helpers; public static class HelperInfo { public static string Name { get; } = \"A\"; }"),
            helperA);
        File.Copy(
            Emit("Resolve.Helpers", "namespace Generator.Helpers; public static class HelperInfo { public static string Name { get; } = \"B\"; }"),
            helperB,
            overwrite: true);

        var main = Emit("ResolveMain", "public static class MainType { public static int X => 1; }");
        var loader = new InProcessAnalyzerAssemblyLoader();
        loader.AddDependencyLocation(helperA);
        loader.AddDependencyLocation(helperB);
        _ = loader.LoadFromPath(main);

        Assert.Equal(2, loader.SnapshotDependencyLocations().Count);
        try
        {
            Assembly.Load("Resolve.Helpers, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
        }
        catch (FileNotFoundException)
        {
            // expected: resolver must not bind
        }

        Assert.Contains(
            loader.SnapshotResolveAttempts(),
            a => a.RequestedName.Contains("Resolve.Helpers", StringComparison.OrdinalIgnoreCase)
                && (a.Outcome.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                    || a.Outcome.Contains("rejected", StringComparison.OrdinalIgnoreCase)));
        Assert.Throws<AnalyzerIdentityCollisionException>(() =>
        {
            var other = Emit("ResolveMain", "public static class MainType { public static int X => 2; }");
            _ = loader.LoadFromPath(other);
        });
    }

    [Fact]
    public void Duplicate_host_contract_copy_is_not_resolved_from_generation_directory()
    {
        var host = typeof(Compilation).Assembly.Location;
        Assert.False(string.IsNullOrWhiteSpace(host));
        var generationDir = Path.Combine(_root, "generation");
        Directory.CreateDirectory(generationDir);
        var privateCopy = Path.Combine(generationDir, "Microsoft.CodeAnalysis.dll");
        File.Copy(host, privateCopy);

        var main = Emit("ContractMain", "public static class MainType { public static int X => 1; }");
        var loader = new InProcessAnalyzerAssemblyLoader();
        loader.AddDependencyLocation(privateCopy);
        _ = loader.LoadFromPath(main);
        try
        {
            Assembly.Load(typeof(Compilation).Assembly.FullName!);
        }
        catch (FileLoadException)
        {
            // already loaded by host
        }

        Assert.DoesNotContain(
            loader.SnapshotLoadedAssemblies(),
            a => PathsEqual(a.Location, privateCopy));
    }

    private string Emit(
        string assemblyName,
        string source,
        IReadOnlyList<MetadataReference>? extraReferences = null,
        string assemblyVersion = "0.0.0.0")
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var systemRuntime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        if (File.Exists(systemRuntime))
        {
            references.Add(MetadataReference.CreateFromFile(systemRuntime));
        }

        if (extraReferences is not null)
        {
            references.AddRange(extraReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default));
        compilation = compilation.WithAssemblyName(assemblyName);
        var output = Path.Combine(_root, assemblyName + "-" + Guid.NewGuid().ToString("N")[..8] + ".dll");
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return output;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }
}
