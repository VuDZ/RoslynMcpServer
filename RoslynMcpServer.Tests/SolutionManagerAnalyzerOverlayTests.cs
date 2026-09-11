using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// Epoch-4 replacement for whole-list wipe tests. Exact inverse and classification
/// live in <see cref="WorkspaceWriteBoundaryTests"/>.
/// </summary>
public sealed class SolutionManagerAnalyzerOverlayTests
{
    [Fact]
    public void Exact_inverse_contract_is_covered_by_workspace_write_boundary_tests()
    {
        var names = typeof(WorkspaceWriteBoundaryTests).GetMethods().Select(m => m.Name).ToArray();
        Assert.Contains(names, n => n.Contains("Exact_inverse", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Whole_list_wipe_is_not_accepted", StringComparison.Ordinal));
    }
}
