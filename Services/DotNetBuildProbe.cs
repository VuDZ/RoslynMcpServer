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
        bool BudgetExhausted = false);

    public static async Task<ProbeResult> RunAsync(
        string projectOrSolutionPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? overallBudget = null,
        TimeSpan? stepTimeout = null)
    {
        var budget = overallBudget ?? DefaultOverallBudget;
        var perStep = stepTimeout ?? DefaultStepTimeout;
        var quoted = $"\"{projectOrSolutionPath}\"";
        var log = new StringBuilder();
        var steps = new List<string>();
        var lastExitCode = 0;
        var timedOut = false;
        var budgetExhausted = false;
        var pin = DotNetSdkEnvironment.TryGetPin(workingDirectory);
        var sw = Stopwatch.StartNew();

        async Task<DotNetCliRunner.RunResult?> RunStepAsync(string label, string arguments)
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
            if (run.TimedOut)
            {
                timedOut = true;
                steps[^1] = $"{label} (TIMED OUT after {timeout.TotalSeconds:0}s)";
            }

            AppendSection(log, steps[^1], run);
            return run;
        }

        await RunStepAsync("dotnet build -v:minimal", $"build {quoted} -v:minimal").ConfigureAwait(false);

        if (!timedOut
            && TryBuildPinnedMsBuildRestoreArguments(pin, projectOrSolutionPath) is { } pinnedRestore
            && ShouldRunPinnedMsBuildRestore(log.ToString(), workingDirectory))
        {
            await RunStepAsync("dotnet exec MSBuild /restore (pinned SDK)", pinnedRestore)
                .ConfigureAwait(false);
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            var restore = await RunStepAsync("dotnet restore -v:minimal", $"restore {quoted} -v:minimal")
                .ConfigureAwait(false);
            if (restore is not null && !timedOut && ShouldRunDetailedRestore(log.ToString(), restore))
            {
                await RunStepAsync("dotnet restore -v:detailed", $"restore {quoted} -v:detailed")
                    .ConfigureAwait(false);
            }
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            await RunStepAsync("dotnet build -v:normal", $"build {quoted} -v:normal").ConfigureAwait(false);
        }

        if (!timedOut && ShouldRunMoreDiagnostics(log.ToString()))
        {
            await RunStepAsync("dotnet build -v:detailed", $"build {quoted} -v:detailed").ConfigureAwait(false);
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

        return new ProbeResult(lastExitCode, combinedLog, metadata, steps, timedOut, budgetExhausted);
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
