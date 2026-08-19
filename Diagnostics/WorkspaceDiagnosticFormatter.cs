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

    public static bool IsSoftWorkspaceAdvisory(string message) =>
        IsNuGetAuditAdvisory(message) || IsNuGetPruneAdvisory(message) || IsNuGetCompatAdvisory(message);

    /// <summary>
    /// True when a formatted diagnostic should fail <c>load_workspace</c>.
    /// Does not treat the word "failed" inside the MSBuildWorkspace wrapper
    /// (<c>Msbuild failed when processing the file</c>) as fatal by itself.
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

        if (HasExplicitErrorToken(formattedDiagnostic))
        {
            return true;
        }

        if (HasMsBuildWarningSeverity(body))
        {
            return false;
        }

        return formattedDiagnostic.StartsWith("Failure:", StringComparison.OrdinalIgnoreCase);
    }

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
        || text.Contains(" warning NETSDK", StringComparison.OrdinalIgnoreCase);

    private static string StripKindPrefix(string formatted)
    {
        var colon = formatted.IndexOf(':');
        return colon >= 0 && colon < formatted.Length - 1
            ? formatted[(colon + 1)..].Trim()
            : formatted;
    }
}
