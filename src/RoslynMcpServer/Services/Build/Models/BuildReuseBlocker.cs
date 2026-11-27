namespace RoslynMcpServer.Services.Build;

/// <summary>Reasons a stored build cannot be reused. Skip stays denied while any of these apply.</summary>
internal static class BuildReuseBlocker
{
    public const string InputContentHashAbsent = "input-content-hash-absent";

    public const string CoverageUnknown = "coverage-unknown";

    public const string PendingInput = "pending-input";

    public const string PathNotInIndex = "path-not-in-index";

    public const string MetadataOnlyDependency = "metadata-only-dependency";

    public const string MembershipUnknown = "membership-unknown";

    public const string ProviderIncomplete = "provider-incomplete";

    public const string OutputPathNotUnique = "output-path-not-unique";

    public const string NoBuildProof = "no-build-proof";
}
