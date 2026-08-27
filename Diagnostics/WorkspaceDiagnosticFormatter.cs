using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Normalizes MSBuildWorkspace diagnostic text for MCP responses.</summary>
public static class WorkspaceDiagnosticFormatter
{
    public static string Format(string kind, string message)
    {
        if (IsNuGetAuditAdvisory(message))
        {
            return $"Warning (NuGet audit): {message}";
        }

        if (IsNuGetPruneAdvisory(message))
        {
            return $"Warning (NuGet prune): {message}";
        }

        if (IsNuGetCompatAdvisory(message))
        {
            return $"Warning (NuGet compat): {message}";
        }

        if (IsMsBuildDesignTimeAdvisory(message))
        {
            return $"Warning (MSBuild design-time): {message}";
        }

        return $"{kind}: {message}";
    }

    public static bool IsNuGetAuditAdvisory(string message) =>
        message.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NU190", StringComparison.OrdinalIgnoreCase)
        || (message.Contains("vulnerabilit", StringComparison.OrdinalIgnoreCase)
            && message.Contains("Package ", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Unused PackageReference / prune-package design-time advisories that MSBuildWorkspace
    /// often reports as <c>WorkspaceDiagnosticKind.Failure</c> even though <c>dotnet</c> prints them as warnings.
    /// </summary>
    public static bool IsNuGetPruneAdvisory(string message) =>
        message.Contains("will not be pruned", StringComparison.OrdinalIgnoreCase)
        || message.Contains("prune package", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Package TFM mismatch (NU1701): netfx/netstandard assets restored into a netcore/net10 project.
    /// MSBuildWorkspace wraps these as <c>Msbuild failed when processing the file</c> even when
    /// <c>dotnet build</c> treats them as warnings.
    /// </summary>
    public static bool IsNuGetCompatAdvisory(string message) =>
        message.Contains("NU1701", StringComparison.OrdinalIgnoreCase)
        || (message.Contains("was restored using", StringComparison.OrdinalIgnoreCase)
            && message.Contains("instead of the project target framework", StringComparison.OrdinalIgnoreCase)
            && message.Contains("may not be fully compatible", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Design-time MSBuild/Roslyn advisories that MSBuildWorkspace often wraps as
    /// <c>Msbuild failed when processing the file</c> with <c>WorkspaceDiagnosticKind.Failure</c>
    /// even though <c>dotnet build</c> treats them as warnings (ASPDEPR007, MSB3270, analyzer refs).
    /// </summary>
    public static bool IsMsBuildDesignTimeAdvisory(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (IsHardMsBuildLoadFailure(message) || HasExplicitErrorToken(message))
        {
            return false;
        }

        return HasMsBuildWarningSeverity(message)
               || IsNamedDesignTimeAdvisory(message)
               || IsMsBuildFailedWrapper(message);
    }

    public static bool IsSoftWorkspaceAdvisory(string message) =>
        IsNuGetAuditAdvisory(message)
        || IsNuGetPruneAdvisory(message)
        || IsNuGetCompatAdvisory(message)
        || IsMsBuildDesignTimeAdvisory(message);

    /// <summary>
    /// Design-time evaluation left <c>TargetFramework</c> empty (typical of Bazel-generated csproj
    /// or a solution opened without the IDE Configuration/Platform). Remains a blocking load failure.
    /// </summary>
    public static bool IsMissingTargetFrameworkEvaluation(string message) =>
        message.Contains("ResolvePackageAssets", StringComparison.OrdinalIgnoreCase)
        && message.Contains("TargetFramework", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Outer CrossTargeting evaluation (<c>TargetFrameworks</c> set, <c>TargetFramework</c> empty) has no
    /// <c>Compile</c> target. Remains a blocking load failure unless the caller retries with an inner TFM.
    /// </summary>
    public static bool IsMissingCompileTarget(string message) =>
        message.Contains("does not contain 'Compile' target", StringComparison.OrdinalIgnoreCase)
        || message.Contains("does not contain \"Compile\" target", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Inner MSBuild text that must still fail <c>load_workspace</c> even inside the
    /// <c>Msbuild failed when processing the file</c> wrapper.
    /// </summary>
    public static bool IsHardMsBuildLoadFailure(string message) =>
        IsMissingTargetFrameworkEvaluation(message)
        || IsMissingCompileTarget(message)
        || message.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)
        || message.Contains("The imported project was not found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NETSDK1045", StringComparison.OrdinalIgnoreCase)
        || (message.Contains("The SDK", StringComparison.OrdinalIgnoreCase)
            && message.Contains("could not be found", StringComparison.OrdinalIgnoreCase));

    private static readonly Regex RxProcessedProjectPath = new(
        @"processing the file '(?<path>[^']+)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? TryGetProcessedProjectPath(string message)
    {
        var m = RxProcessedProjectPath.Match(message);
        return m.Success ? m.Groups["path"].Value : null;
    }

    /// <summary>
    /// True when a formatted diagnostic should fail <c>load_workspace</c>.
    /// Does not treat the word "failed" inside the MSBuildWorkspace wrapper
    /// (<c>Msbuild failed when processing the file</c>) as fatal by itself.
    /// Wrapped messages without an explicit error code or known-hard inner text are warnings.
    /// </summary>
    public static bool IsBlockingLoadFailure(string formattedDiagnostic)
    {
        if (string.IsNullOrWhiteSpace(formattedDiagnostic))
        {
            return false;
        }

        var body = StripKindPrefix(formattedDiagnostic);
        if (IsSoftWorkspaceAdvisory(body) || IsSoftWorkspaceAdvisory(formattedDiagnostic))
        {
            return false;
        }

        if (HasExplicitErrorToken(formattedDiagnostic) || HasExplicitErrorToken(body))
        {
            return true;
        }

        if (IsHardMsBuildLoadFailure(body) || IsHardMsBuildLoadFailure(formattedDiagnostic))
        {
            return true;
        }

        if (HasMsBuildWarningSeverity(body))
        {
            return false;
        }

        return formattedDiagnostic.StartsWith("Failure:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamedDesignTimeAdvisory(string message) =>
        message.Contains("IncludeOpenAPIAnalyzers", StringComparison.OrdinalIgnoreCase)
        || message.Contains("deprecated and will be removed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("mismatch between the processor architecture", StringComparison.OrdinalIgnoreCase)
        || message.Contains(
            "Found project reference without a matching metadata reference",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsMsBuildFailedWrapper(string message) =>
        message.Contains("Msbuild failed when processing the file", StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitErrorToken(string text) =>
        text.StartsWith("error", StringComparison.OrdinalIgnoreCase)
        || text.Contains(": error", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" error NU", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" error MSB", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" error NETSDK", StringComparison.OrdinalIgnoreCase);

    private static bool HasMsBuildWarningSeverity(string text) =>
        text.StartsWith("warning ", StringComparison.OrdinalIgnoreCase)
        || text.Contains(": warning ", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" warning NU", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" warning MSB", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" warning NETSDK", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" warning ASP", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ASPDEPR", StringComparison.OrdinalIgnoreCase);

    private static string StripKindPrefix(string formatted)
    {
        var colon = formatted.IndexOf(':');
        return colon >= 0 && colon < formatted.Length - 1
            ? formatted[(colon + 1)..].Trim()
            : formatted;
    }
}
