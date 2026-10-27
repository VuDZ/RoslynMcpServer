namespace RoslynMcpServer.Tests.Support;

/// <summary>Values of the xUnit <c>Category</c> trait that CI filters by.</summary>
internal static class TestCategories
{
    /// <summary>
    /// The test checks behavior that exists only on Windows. CI excludes it outside Windows; the exact
    /// list is pinned by <c>WindowsOnlyTestInventoryTests</c>.
    /// </summary>
    internal const string RequiresWindows = "RequiresWindows";

    /// <summary>
    /// The test builds a fixture with the exact SDK of the deferred workspace-load-cache experiment
    /// (<c>WorkspaceLoadCache.PinnedSdk</c>). The main suite and CI exclude it until the experiment resumes;
    /// the exact list is pinned by <c>PinnedSdkTestInventoryTests</c>.
    /// </summary>
    internal const string RequiresPinnedSdk = "RequiresPinnedSdk";
}
