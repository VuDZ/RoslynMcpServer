namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Compiles the assemblies of the neighbor-map cases once and lays them out on disk anew for every case:
/// which copy lies in which directory is what the cases differ in.
/// </summary>
public sealed class IlNeighborFixture : IDisposable
{
    public IlNeighborFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "RoslynMcpIlNeighbors-" + Guid.NewGuid().ToString("N"));
        var compiled = Path.Combine(_root, "compiled");
        _lib = IlFixtureCompiler.Compile("Lib", IlFixtureSources.NeighborLib, compiled);
        _tests = IlFixtureCompiler.Compile("Tests", IlFixtureSources.NeighborTests, compiled);
        _other = IlFixtureCompiler.Compile("Other", IlFixtureSources.NeighborOther, compiled);
        _package = IlFixtureCompiler.Compile("Package", IlFixtureSources.NeighborPackage, compiled);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// A new directory tree in which <c>Lib</c>, <c>Tests</c> and <c>Other</c> are built into separate
    /// directories and no directory holds a copy of anything else.
    /// </summary>
    internal IlNeighborLayout NewLayout()
    {
        var root = Path.Combine(_root, "layout-" + Interlocked.Increment(ref _layouts));
        return new IlNeighborLayout(root, _lib, _tests, _other, _package);
    }

    private readonly string _root;
    private readonly string _lib;
    private readonly string _tests;
    private readonly string _other;
    private readonly string _package;
    private int _layouts;
}
