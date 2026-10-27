namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// The exact SDK the workspace-load-cache fixtures were observed with. Their expected results (imports, resolved
/// packs, generated output) belong to that toolchain, so a newer SDK is no substitute. The tests that build with it
/// are in <see cref="Support.TestCategories.RequiresPinnedSdk"/>, which the main suite excludes while the
/// experiment is deferred.
/// </summary>
internal static class PinnedSdk
{
    public const string Version = "10.0.300";

    /// <summary>global.json content that selects exactly <see cref="Version"/>.</summary>
    public const string GlobalJson = "{\"sdk\":{\"version\":\"" + Version + "\",\"rollForward\":\"disable\"}}";
}
