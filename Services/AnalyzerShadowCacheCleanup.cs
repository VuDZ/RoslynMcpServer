namespace RoslynMcpServer.Services;

/// <summary>
/// Operator cleanup for a shared per-solution shadow root. Published generations are never deleted
/// by workspace clear/dispose. Callers must confirm every owning process has stopped.
/// Delete is allowed only under known cache parents (containment); a confirmed-stop flag alone
/// does not authorize deleting an arbitrary path.
/// </summary>
public static class AnalyzerShadowCacheCleanup
{
    public const string Procedure =
        "1. Identify the concrete shadow root and every owning MCP/test-host process. "
        + "2. Stop all of those processes and any dependent operations. "
        + "3. Confirm the resolved absolute target, containment under the expected cache parent, and ownership. "
        + "4. Delete only that chosen root (or chosen generation directories). "
        + "5. On the next opt-in load, prepare generations again from a completed build. "
        + "Do not delete published files if owner stop was not confirmed. "
        + "A single reset_workspace or a vanished PID is not sufficient proof.";

    public static IReadOnlyList<string> AllowedParentDirectories()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        return
        [
            Path.Combine(temp, "RoslynMcpServer.AnalyzerShadowCopy"),
            Path.Combine(temp, "RoslynMcpServer.Tests"),
            Path.Combine(temp, "RoslynMcpServer.Epoch1"),
        ];
    }

    public static bool TryDeleteRoot(string rootDirectory, bool allOwningProcessesStopped, out string reason)
    {
        if (!allOwningProcessesStopped)
        {
            reason = "owners-not-confirmed-stopped";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            reason = "root-empty";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(rootDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "root-unresolvable: " + ex.Message;
            return false;
        }

        if (!IsUnderAllowedParent(full))
        {
            reason = "outside-allowed-cache-parent";
            return false;
        }

        if (!Directory.Exists(full))
        {
            reason = "root-missing";
            return true;
        }

        try
        {
            Directory.Delete(full, recursive: true);
            reason = "deleted";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = "delete-failed: " + ex.Message;
            return false;
        }
    }

    internal static bool IsUnderAllowedParent(string resolvedRoot)
    {
        foreach (var parent in AllowedParentDirectories())
        {
            if (AnalyzerShadowPathSafety.IsContained(resolvedRoot, parent))
            {
                return true;
            }
        }

        return false;
    }
}
