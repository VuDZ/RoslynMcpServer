using System.Reflection;
using System.Text;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Captured MSBuild / Visual Studio discovery data for surfacing in LoadWorkspace failures.
/// </summary>
public static class MsBuildEnvironmentInfo
{
    public static IReadOnlyList<string> QueriedInstanceLines { get; set; } = Array.Empty<string>();

    public static string? RegisteredInstanceName { get; set; }

    public static string? RegisteredInstanceVersion { get; set; }

    public static string? RegisteredMsBuildPath { get; set; }

    public static string? RegisteredVisualStudioRootPath { get; set; }

    /// <summary>
    /// Reads whichever "registered instance" property exists on <see cref="MSBuildLocator"/> for this package version.
    /// </summary>
    public static void RefreshRegisteredInstance()
    {
        RegisteredInstanceName = null;
        RegisteredInstanceVersion = null;
        RegisteredMsBuildPath = null;
        RegisteredVisualStudioRootPath = null;

        var locatorType = typeof(MSBuildLocator);
        object? registered = null;
        foreach (var propName in new[] { "RegisteredVisualStudioInstance", "RegisteredInstance" })
        {
            var prop = locatorType.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
            registered = prop?.GetValue(null);
            if (registered is not null)
            {
                break;
            }
        }

        if (registered is null)
        {
            return;
        }

        var regType = registered.GetType();
        RegisteredInstanceName = regType.GetProperty("Name")?.GetValue(registered) as string;
        RegisteredInstanceVersion = regType.GetProperty("Version")?.GetValue(registered)?.ToString();
        RegisteredMsBuildPath = regType.GetProperty("MSBuildPath")?.GetValue(registered) as string;
        RegisteredVisualStudioRootPath = regType.GetProperty("VisualStudioRootPath")?.GetValue(registered) as string;
    }

    public static string FormatMarkdownSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("### MSBuild environment (Locator)");
        sb.AppendLine();
        sb.AppendLine("**Queried instances (before RegisterDefaults):**");
        if (QueriedInstanceLines.Count == 0)
        {
            sb.AppendLine("- (none reported)");
        }
        else
        {
            foreach (var line in QueriedInstanceLines)
            {
                sb.AppendLine($"- {line}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("**Registered instance (after RegisterDefaults):**");
        if (RegisteredInstanceName is null && RegisteredMsBuildPath is null)
        {
            sb.AppendLine("- (not reported by this MSBuild.Locator version — see stderr `[DEBUG]` lines at startup)");
        }
        else
        {
            sb.AppendLine($"- **Name:** `{RegisteredInstanceName ?? "(unknown)"}`");
            sb.AppendLine($"- **Version:** `{RegisteredInstanceVersion ?? "(unknown)"}`");
            sb.AppendLine($"- **MSBuildPath:** `{RegisteredMsBuildPath ?? "(unknown)"}`");
            sb.AppendLine($"- **VisualStudioRootPath:** `{RegisteredVisualStudioRootPath ?? "(unknown)"}`");
        }

        return sb.ToString();
    }
}
