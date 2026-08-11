using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Runs <c>dotnet build</c> with SDK pinning and escalates verbosity when diagnostics are missing.
/// Overall wall-clock budget prevents multi-step NuGet hangs (~15 min).
/// </summary>
public static class DotNetBuildProbe
{
    public static readonly TimeSpan DefaultOverallBudget = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(180);

    public sealed record ProbeResult(
        int ExitCode,
        string CombinedOutput,
        string RunMetadata,
        IReadOnlyList<string> StepsExecuted,
        bool TimedOut = false,
        bool BudgetExhausted = false,
        bool NoIncremental = true);

    public static async Task<ProbeResult> RunAsync(
        string projectOrSolutionPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? overallBudget = null,
        TimeSpan? stepTimeout = null,
        string? configuration = null,
        bool noIncremental = true)
    {
        var budget = overallBudget ?? DefaultOverallBudget;
        var perStep = stepTimeout ?? DefaultStepTimeout;
        var quoted = $"\"{projectOrSolutionPath}\"";
        var configSwitch = DotNetConfigurationArguments.FormatSwitch(configuration);
        var incrementalSwitch = noIncremental ? " --no-incremental" : string.Empty;
        var log = new StringBuilder();
        var steps = new List<string>();
        var buildExitCodes = new List<int>();
        var lastExitCode = 0;
        var timedOut = false;
        var budgetExhausted = false;
        var pin = DotNetSdkEnvironment.TryGetPin(workingDirectory);
        var sw = Stopwatch.StartNew();

        async Task<DotNetCliRunner.RunResult?> RunStepAsync(string label, string arguments, bool isBuildStep)
        {
            if (sw.Elapsed >= budget)
            {
                budgetExhausted = true;
                steps.Add($"{label} (skipped: overall budget {budget.TotalSeconds:0}s exhausted)");
                return null;
            }

            var remaining = budget - sw.Elapsed;
            var timeout = remaining < perStep ? remaining : perStep;
            if (timeout <= TimeSpan.Zero)
            {
                budgetExhausted = true;
                steps.Add($"{label} (skipped: no remaining budget)");
                return null;
            }

            steps.Add(label);
            var run = await DotNetCliRunner.RunWithMetadataAsync(arguments, workingDirectory, cancellationToken, timeout)
                .ConfigureAwait(false);
            lastExitCode = run.ExitCode;
            if (isBuildStep)
            {
                buildExitCodes.Add(run.ExitCode);
            }

            if (run.TimedOut)
            {
                timedOut = true;
                steps[^1] = $"{label} (TIMED OUT after {timeout.TotalSeconds:0}s)";
            }

            AppendSection(log, steps[^1], run);
            return run;
        }

        await RunStepAsync(
                $"dotnet build -v:minimal{configSwitch}{incrementalSwitch}",
                $"build {quoted} -v:minimal{configSwitch}{incrementalSwitch}",
                isBuildStep: true)
            .ConfigureAwait(false);

        if (!timedOut
            && TryBuildPinnedMsBuildRestoreArguments(pin, projectOrSolutionPath) is { } pinnedRestore
            && ShouldRunPinnedMsBuildRestore(log.ToString(), workingDirectory))
        {
            await RunStepAsync("dotnet exec MSBuild /restore (pinned SDK)", pinnedRestore, isBuildStep: false)
                .ConfigureAwait(false);
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            var restore = await RunStepAsync("dotnet restore -v:minimal", $"restore {quoted} -v:minimal", isBuildStep: false)
                .ConfigureAwait(false);
            if (restore is not null && !timedOut && ShouldRunDetailedRestore(log.ToString(), restore))
            {
                await RunStepAsync("dotnet restore -v:detailed", $"restore {quoted} -v:detailed", isBuildStep: false)
                    .ConfigureAwait(false);
            }
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            await RunStepAsync(
                    $"dotnet build -v:normal{configSwitch}{incrementalSwitch}",
                    $"build {quoted} -v:normal{configSwitch}{incrementalSwitch}",
                    isBuildStep: true)
                .ConfigureAwait(false);
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            await RunStepAsync(
                    $"dotnet build -v:detailed{configSwitch}{incrementalSwitch}",
                    $"build {quoted} -v:detailed{configSwitch}{incrementalSwitch}",
                    isBuildStep: true)
                .ConfigureAwait(false);
        }

        if (budgetExhausted)
        {
            log.AppendLine();
            log.AppendLine(
                $"--- MCP_BUILD_PROBE_BUDGET --- overall budget {budget.TotalSeconds:0}s exhausted after {sw.Elapsed.TotalSeconds:0}s; further escalate steps skipped.");
        }

        var combinedLog = log.ToString().TrimEnd();
        var metadata = await DotNetCliRunner.CreateRunMetadataAsync(workingDirectory, combinedLog, CancellationToken.None)
            .ConfigureAwait(false);
        var effectiveExit = ComputeEffectiveBuildExitCode(buildExitCodes, lastExitCode, combinedLog);

        return new ProbeResult(
            effectiveExit,
            combinedLog,
            metadata,
            steps,
            timedOut,
            budgetExhausted,
            noIncremental);
    }

    /// <summary>
    /// Use the <b>last</b> <c>dotnet build</c> exit (so restore+rebuild escalate can recover),
    /// never a restore-only exit that would mask a failed build when no rebuild ran.
    /// When no build steps completed, fall back to the last step exit / log sections.
    /// </summary>
    internal static int ComputeEffectiveBuildExitCode(
        IReadOnlyList<int> buildExitCodes,
        int lastStepExitCode,
        string combinedLog)
    {
        if (buildExitCodes.Count > 0)
        {
            return buildExitCodes[^1];
        }

        if (TryGetFirstFailedBuildSectionExitCode(combinedLog) is { } sectionCode)
        {
            return sectionCode;
        }

        // No build step completed (budget/timeout) — fall back to last step exit.
        return lastStepExitCode;
    }

    private static readonly Regex BuildSectionExit = new(
        @"---\s+dotnet build[^\r\n]*\(exit\s+(?<code>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns the first non-zero exit from a <c>dotnet build</c> section header, if any.
    /// Used when in-memory build exit codes were not recorded (should be rare).
    /// </summary>
    internal static int? TryGetFirstFailedBuildSectionExitCode(string combinedLog)
    {
        if (string.IsNullOrEmpty(combinedLog))
        {
            return null;
        }

        foreach (Match match in BuildSectionExit.Matches(combinedLog))
        {
            if (int.TryParse(match.Groups["code"].Value, out var code) && code != 0)
            {
                return code;
            }
        }

        return null;
    }

    internal static string? TryBuildPinnedMsBuildRestoreArguments(
        DotNetSdkEnvironment.SdkPinInfo? pin,
        string projectOrSolutionPath)
    {
        if (pin?.MsBuildDllPath is null || !File.Exists(pin.MsBuildDllPath))
        {
            return null;
        }

        return $"exec \"{pin.MsBuildDllPath}\" \"{projectOrSolutionPath}\" /restore /v:detailed /nologo";
    }

    private static readonly Regex SectionNonZeroExit = new(
        @"\(exit\s+(?<code>[1-9]\d*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool ShouldRunPinnedMsBuildRestore(string combinedSoFar, string workingDirectory)
    {
        if (!ShouldRunMoreDiagnostics(combinedSoFar))
        {
            return false;
        }

        var pin = DotNetSdkEnvironment.TryGetPin(workingDirectory);
        if (pin?.MsBuildDllPath is null)
        {
            return false;
        }

        var logMsbuild = MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedSoFar);
        if (!string.IsNullOrEmpty(logMsbuild))
        {
            return !DotNetSdkEnvironment.PathsEqual(logMsbuild, pin.MsBuildDllPath);
        }

        return combinedSoFar.Contains("-- FAILED", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldRunMoreDiagnostics(string combinedSoFar)
    {
        var errors = DotNetBuildDiagnosticParser.Parse(combinedSoFar)
            .Count(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
        if (errors > 0)
        {
            return false;
        }

        if (SectionNonZeroExit.IsMatch(combinedSoFar))
        {
            return true;
        }

        return combinedSoFar.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
               || combinedSoFar.Contains("-- FAILED", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldRunDetailedRestore(string combinedSoFar, DotNetCliRunner.RunResult restoreRun)
    {
        if (!ShouldRunMoreDiagnostics(combinedSoFar))
        {
            return false;
        }

        return restoreRun.ExitCode != 0 && string.IsNullOrWhiteSpace(restoreRun.CombinedOutput);
    }

    /// <summary>
    /// Formats the incremental CLI switch for build steps (unit-testable).
    /// </summary>
    internal static string FormatIncrementalSwitch(bool noIncremental) =>
        noIncremental ? " --no-incremental" : string.Empty;

    private static void AppendSection(StringBuilder log, string label, DotNetCliRunner.RunResult run)
    {
        if (log.Length > 0)
        {
            log.AppendLine();
        }

        log.AppendLine(
            $"--- {label} (exit {run.ExitCode}, stdout {run.StdOutLength} chars, stderr {run.StdErrLength} chars) ---");

        if (!string.IsNullOrWhiteSpace(run.CombinedOutput))
        {
            log.AppendLine(run.CombinedOutput.TrimEnd());
        }
        else
        {
            log.AppendLine(
                $"(no stdout/stderr text captured; exit code {run.ExitCode}. A follow-up step may use -v:detailed.)");
        }
    }
}
