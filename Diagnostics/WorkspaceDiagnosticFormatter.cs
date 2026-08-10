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

    public static bool IsSoftWorkspaceAdvisory(string message) =>
        IsNuGetAuditAdvisory(message) || IsNuGetPruneAdvisory(message);

    public static bool IsBlockingLoadFailure(string formattedDiagnostic) =>
        !IsSoftWorkspaceAdvisory(StripKindPrefix(formattedDiagnostic))
        && (formattedDiagnostic.Contains("Failure", StringComparison.OrdinalIgnoreCase)
            || formattedDiagnostic.Contains(": error", StringComparison.OrdinalIgnoreCase)
            || formattedDiagnostic.StartsWith("error", StringComparison.OrdinalIgnoreCase));

    private static string StripKindPrefix(string formatted)
    {
        var colon = formatted.IndexOf(':');
        return colon >= 0 && colon < formatted.Length - 1
            ? formatted[(colon + 1)..].Trim()
            : formatted;
    }
}
