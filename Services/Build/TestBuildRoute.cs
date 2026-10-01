namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Resolves <c>buildPolicy</c> and <c>noBuild</c> for one test-tool call.
/// On the DLL route, omitting both means <see cref="TestBuildPolicy.Auto"/>.
/// Without <c>binariesPath</c>, a null <c>noBuild</c> keeps that tool's historical default.
/// </summary>
internal sealed class TestBuildRoute
{
    public bool DllRoute { get; }

    public TestBuildPolicy Policy { get; }

    public bool NoBuild { get; }

    public string? Error { get; }

    private TestBuildRoute(bool dllRoute, TestBuildPolicy policy, bool noBuild, string? error)
    {
        DllRoute = dllRoute;
        Policy = policy;
        NoBuild = noBuild;
        Error = error;
    }

    public static TestBuildRoute Resolve(
        bool dllRoute,
        string? buildPolicy,
        bool? noBuild,
        bool omittedNoBuildSkipsRebuild)
    {
        var policyText = string.IsNullOrWhiteSpace(buildPolicy) ? null : buildPolicy.Trim();
        var policyPassed = policyText is not null;
        var noBuildPassed = noBuild.HasValue;

        if (!dllRoute)
        {
            if (policyPassed)
            {
                return Fail("Error: `buildPolicy` requires `binariesPath`.");
            }

            return NonDll(noBuild ?? omittedNoBuildSkipsRebuild);
        }

        if (policyPassed && noBuildPassed)
        {
            return Fail("Error: pass `buildPolicy` or `noBuild` on the DLL route, not both.");
        }

        if (noBuildPassed)
        {
            return Dll(noBuild!.Value ? TestBuildPolicy.Never : TestBuildPolicy.Always);
        }

        if (policyText is null)
        {
            return Dll(TestBuildPolicy.Auto);
        }

        if (!TryParse(policyText, out var policy))
        {
            return Fail("Error: `buildPolicy` must be `auto`, `always`, or `never`.");
        }

        return Dll(policy);
    }

    private static bool TryParse(string text, out TestBuildPolicy policy)
    {
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            policy = TestBuildPolicy.Auto;
            return true;
        }

        if (text.Equals("always", StringComparison.OrdinalIgnoreCase))
        {
            policy = TestBuildPolicy.Always;
            return true;
        }

        if (text.Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            policy = TestBuildPolicy.Never;
            return true;
        }

        policy = default;
        return false;
    }

    private static TestBuildRoute NonDll(bool noBuild) =>
        new(dllRoute: false, TestBuildPolicy.Auto, noBuild, error: null);

    private static TestBuildRoute Dll(TestBuildPolicy policy) =>
        new(dllRoute: true, policy, noBuild: false, error: null);

    private static TestBuildRoute Fail(string error) =>
        new(dllRoute: false, TestBuildPolicy.Auto, noBuild: false, error);
}
