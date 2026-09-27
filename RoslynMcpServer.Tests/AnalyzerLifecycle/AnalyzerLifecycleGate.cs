namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Local opt-in for the MSBuild lifecycle host tests. The CI job <c>lifecycle</c> sets the
/// variable; no MCP tool does, so an agent cannot start the host through <c>run_specific_test</c>.
/// </summary>
internal static class AnalyzerLifecycleGate
{
    internal const string EnvironmentVariable = "ROSLYN_MCP_ANALYZER_LIFECYCLE";
    internal const string EnabledValue = "1";

    /// <summary>
    /// Returns the skip reason when the suite is not opted in, or <see langword="null"/> when the
    /// host probe should decide. Checked before the probe so a disabled run never starts a host process.
    /// </summary>
    internal static string? GetSkipReason()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.Equals(value, EnabledValue, StringComparison.Ordinal))
        {
            return null;
        }

        return "Set " + EnvironmentVariable + "=" + EnabledValue
            + " to run the AnalyzerLifecycle MSBuild host suite (the CI job `lifecycle` sets it); "
            + "current value: " + (value is null ? "(unset)" : "'" + value + "'") + ".";
    }
}
