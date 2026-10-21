namespace RoslynMcpServer.Tests.Support;

/// <summary>Values of the xUnit <c>Category</c> trait that CI filters by.</summary>
internal static class TestCategories
{
    /// <summary>
    /// The test checks behavior that exists only on Windows. CI excludes it outside Windows; the exact
    /// list is pinned by <c>WindowsOnlyTestInventoryTests</c>.
    /// </summary>
    internal const string RequiresWindows = "RequiresWindows";
}
