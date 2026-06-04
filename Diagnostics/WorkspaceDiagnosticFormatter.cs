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

        return $"{kind}: {message}";
    }

    public static bool IsNuGetAuditAdvisory(string message) =>
        message.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NU190", StringComparison.OrdinalIgnoreCase)
        || (message.Contains("vulnerabilit", StringComparison.OrdinalIgnoreCase)
            && message.Contains("Package ", StringComparison.OrdinalIgnoreCase));

    public static bool IsBlockingLoadFailure(string formattedDiagnostic) =>
        !IsNuGetAuditAdvisory(StripKindPrefix(formattedDiagnostic))
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
