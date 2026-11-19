using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// One arrangement of the neighbor-map solution on disk: three projects, each with its output assembly in
/// its own directory, and the copies a case puts next to them.
/// </summary>
internal sealed class IlNeighborLayout
{
    public const string LibAssembly = "Lib";
    public const string TestsAssembly = "Tests";
    public const string OtherAssembly = "Other";
    public const string PackageAssembly = "Package";

    public static IlTypeName LibType { get; } = new(LibAssembly, "Lib.LibType");

    public static IlTypeName OtherType { get; } = new(OtherAssembly, "Other.OtherType");

    public static IlTypeName LoaderTests { get; } = new(TestsAssembly, "Tests.LoaderTests");

    public static IlTypeName PlainTests { get; } = new(TestsAssembly, "Tests.PlainTests");

    public string LibPath { get; }

    public string TestsPath { get; }

    public string OtherPath { get; }

    /// <summary>An assembly that is not a project of the solution and is never among the inputs.</summary>
    public string PackageSource { get; }

    public string TestsDirectory => Path.GetDirectoryName(TestsPath)!;

    public string OtherDirectory => Path.GetDirectoryName(OtherPath)!;

    /// <summary>The loaded projects: <c>Tests</c> is the only test project.</summary>
    public IReadOnlyList<IlAssemblyInput> Inputs =>
    [
        new IlAssemblyInput(LibPath, IsTestProject: false),
        new IlAssemblyInput(OtherPath, IsTestProject: false),
        new IlAssemblyInput(TestsPath, IsTestProject: true),
    ];

    public IlNeighborLayout(string root, string lib, string tests, string other, string package)
    {
        LibPath = Place(lib, Path.Combine(root, "Lib", "bin"));
        TestsPath = Place(tests, Path.Combine(root, "Tests", "bin"));
        OtherPath = Place(other, Path.Combine(root, "Other", "bin"));
        PackageSource = package;
    }

    /// <summary>Copies an assembly file into a directory, as a build does with a reference.</summary>
    public static string Place(string assemblyPath, string directory)
    {
        Directory.CreateDirectory(directory);
        var copy = Path.Combine(directory, Path.GetFileName(assemblyPath));
        File.Copy(assemblyPath, copy);
        return copy;
    }

    public IlNeighborMap BuildMap() => IlNeighborMapBuilder.Build(Inputs);

    /// <summary>Builds the neighbor map the way a caller does and the graph with it.</summary>
    public IlGraphBuildResult BuildGraph() => IlDependencyGraphBuilder.BuildFromFiles(Inputs, BuildMap());
}
