namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Cached semantic view of the production sources of this repository. The compilation is built once
/// per test process, so the migrated structural checks share one input and one scope statement instead
/// of compiling the tree per test. No MSBuild project loading is involved.
/// </summary>
internal static class ProductionAnalysis
{
    public static SourceSetAnalysis Instance => _instance.Value;

    /// <summary>The production scope is read from the working copy; the compiled binary is not the subject.</summary>
    public static SourceSetAnalysis Create() => SourceSetAnalysis.Create(SourceSetScope.ProductionRepository(RepositoryRoot.Find()));

    private static readonly Lazy<SourceSetAnalysis> _instance = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);
}
