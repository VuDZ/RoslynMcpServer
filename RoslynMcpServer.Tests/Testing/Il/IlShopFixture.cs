using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// A small solution compiled once for the IL graph tests: a library, a plugin assembly that nothing
/// references, a third project and a test project, each in its own output directory. The directory of the
/// test project also holds what a build copies there.
/// </summary>
public sealed class IlShopFixture : IDisposable
{
    internal const string LibAssembly = "Shop.Lib";
    internal const string PluginsAssembly = "Shop.Plugins";
    internal const string OtherAssembly = "Shop.Other";
    internal const string TestsAssembly = "Shop.Tests";

    internal string LibPath { get; }

    internal string PluginsPath { get; }

    internal string OtherPath { get; }

    internal string TestsPath { get; }

    /// <summary>The package assembly: it lies next to the test assembly and is not a project of the solution.</summary>
    internal string PackagePath { get; }

    internal IReadOnlyList<IlAssemblyInput> Inputs { get; }

    /// <summary>The graph of the solution, read from the files on first use.</summary>
    internal IlGraphBuildResult Result => _result.Value;

    public IlShopFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "RoslynMcpIlGraph-" + Guid.NewGuid().ToString("N"));
        var packages = Path.Combine(_root, "packages");
        PackagePath = IlFixtureCompiler.Compile("Fixture.Package", IlFixtureSources.Package, packages);
        var nunit = IlFixtureCompiler.Compile("nunit.framework", IlFixtureSources.NUnitFramework, packages);
        var mstest = IlFixtureCompiler.Compile(
            "Microsoft.VisualStudio.TestPlatform.TestFramework",
            IlFixtureSources.MSTestFramework,
            packages);

        LibPath = IlFixtureCompiler.Compile(LibAssembly, IlFixtureSources.ShopLib, OutputDirectory(LibAssembly), PackagePath);
        PluginsPath = IlFixtureCompiler.Compile(PluginsAssembly, IlFixtureSources.ShopPlugins, OutputDirectory(PluginsAssembly));
        OtherPath = IlFixtureCompiler.Compile(OtherAssembly, IlFixtureSources.ShopOther, OutputDirectory(OtherAssembly));
        TestsPath = IlFixtureCompiler.Compile(
            TestsAssembly,
            IlFixtureSources.ShopTests,
            OutputDirectory(TestsAssembly),
            LibPath,
            PackagePath,
            nunit,
            mstest);

        // What a build leaves on disk: the references of the test project are copied next to the test
        // assembly, and the test assembly itself is copied into the output of a project that references it.
        CopyInto(OutputDirectory(TestsAssembly), LibPath, PluginsPath, PackagePath, nunit, mstest);
        CopyInto(OutputDirectory(OtherAssembly), TestsPath);

        Inputs =
        [
            new IlAssemblyInput(LibPath, IsTestProject: false),
            new IlAssemblyInput(PluginsPath, IsTestProject: false),
            new IlAssemblyInput(OtherPath, IsTestProject: false),
            new IlAssemblyInput(TestsPath, IsTestProject: true),
        ];
        _result = new Lazy<IlGraphBuildResult>(
            () => IlDependencyGraphBuilder.BuildFromFiles(Inputs, IlNeighborMapBuilder.Build(Inputs)));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    internal static IlTypeName Lib(string name) => new(LibAssembly, "Shop." + name);

    internal static IlTypeName Edge(string name) => new(LibAssembly, "Shop.Edges." + name);

    internal static IlTypeName Test(string name) => new(TestsAssembly, "Shop.Tests." + name);

    private string OutputDirectory(string project) => Path.Combine(_root, project, "bin");

    private static void CopyInto(string directory, params string[] files)
    {
        foreach (var file in files)
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
    }

    private readonly string _root;
    private readonly Lazy<IlGraphBuildResult> _result;
}
