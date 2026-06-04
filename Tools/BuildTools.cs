using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class BuildTools
{
    private const int MaxDiagnostics = 20;

    private readonly ILogger<BuildTools> _logger;

    public BuildTools(ILogger<BuildTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_build", Title = "Run dotnet build")]
    [Description("Runs `dotnet build` on the specified project or solution. Use this AFTER editing a file to verify your changes compile successfully.")]
    public async Task<string> RunDotNetBuild(
        [Description("Path to .csproj or .sln (same parameter name as load_workspace / run_dotnet_test).")]
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), $"File not found: `{fullPath}`");
            }

            var extension = Path.GetExtension(fullPath);
            if (!string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), $"Path must be a .csproj or .sln file: `{fullPath}`");
            }

            var workDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath);
            var probe = await DotNetBuildProbe.RunAsync(fullPath, workDir, cancellationToken).ConfigureAwait(false);
            var combined = probe.CombinedOutput;
            var processExitCode = probe.ExitCode;
            var runMetadata = probe.RunMetadata;
            var parsed = DotNetBuildDiagnosticParser.Parse(combined);
            var errorEntries = MergeErrorEntries(
                workDir,
                combined,
                runMetadata,
                parsed);
            var warningEntries = DeduplicateDiagnostics(
                parsed.Where(d => string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase)));

            var display = new List<DotNetBuildDiagnosticParser.DiagnosticEntry>();
            display.AddRange(errorEntries.Take(MaxDiagnostics));
            var remaining = MaxDiagnostics - display.Count;
            if (remaining > 0)
            {
                display.AddRange(warningEntries.Take(remaining));
            }

            var totalMatched = errorEntries.Count + warningEntries.Count;
            var truncated = totalMatched > MaxDiagnostics;

            if (errorEntries.Count == 0 && processExitCode == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(RunDotNetBuild),
                    BuildSuccessReport(runMetadata, probe.StepsExecuted, warningEntries));
            }

            if (errorEntries.Count == 0 && processExitCode != 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(RunDotNetBuild),
                    BuildFailedWithoutParsedDiagnostics(
                        runMetadata,
                        probe.StepsExecuted,
                        processExitCode,
                        combined));
            }

            var errSb = new StringBuilder();
            errSb.AppendLine("## Build failed");
            errSb.AppendLine();
            AppendRunMetadata(errSb, runMetadata, probe.StepsExecuted);
            errSb.AppendLine($"Exit code: `{processExitCode}`. Parsed diagnostics (MSBuild + NuGet NU####):");
            foreach (var d in display)
            {
                errSb.AppendLine($"- **{d.Severity}** `{d.Code}` `{d.Location}` — {d.Message}");
            }

            if (truncated)
            {
                errSb.AppendLine();
                errSb.AppendLine("[!] More than 20 diagnostics reported. Showing the first 20 (errors first) to protect LLM context.");
            }

            if (totalMatched < CountLikelyIssueLines(combined))
            {
                TruncatedProcessLog.AppendLastCharacters(
                    errSb,
                    "Additional console output (truncated):",
                    combined);
            }

            MsBuildLogHighlighter.AppendKeyLinesSection(errSb, combined);
            AppendNuGetAuditHintIfNeeded(errSb, combined);
            return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), errSb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), "Build was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunDotNetBuild failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(RunDotNetBuild),
                $"Failed to run `dotnet build`: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildSuccessReport(
        string runMetadata,
        IReadOnlyList<string> stepsExecuted,
        IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> warningEntries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Build succeeded");
        sb.AppendLine();
        AppendRunMetadata(sb, runMetadata, stepsExecuted);
        sb.AppendLine("No **error** lines matched (MSBuild `path(line,col): error` or NuGet `error NU####`).");
        if (warningEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings:");
            foreach (var d in warningEntries.Take(MaxDiagnostics))
            {
                sb.AppendLine($"- **warning** `{d.Code}` `{d.Location}` — {d.Message}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildFailedWithoutParsedDiagnostics(
        string runMetadata,
        IReadOnlyList<string> stepsExecuted,
        int processExitCode,
        string combined)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Build failed");
        sb.AppendLine();
        AppendRunMetadata(sb, runMetadata, stepsExecuted);
        sb.AppendLine(
            $"Exit code: `{processExitCode}`. No lines matched MSBuild `path(line,col): error|warning CODE` or NuGet `error|warning NU####` patterns (including `: error NU####` and embedded NU lines).");
        sb.AppendLine(
            "Steps: minimal build → restore (minimal, then detailed if empty) → build normal → build detailed. SDK is pinned via `global.json` env vars. See sectioned console output below.");
        MsBuildLogHighlighter.AppendKeyLinesSection(sb, combined);
        TruncatedProcessLog.AppendLastCharacters(
            sb,
            TruncatedProcessLog.BuildPreambleBuildConsoleTail(processExitCode),
            combined);
        AppendNuGetAuditHintIfNeeded(sb, combined);
        return sb.ToString().TrimEnd();
    }

    private static void AppendNuGetAuditHintIfNeeded(StringBuilder sb, string combined)
    {
        if (!DotNetBuildDiagnosticParser.OutputSuggestsNuGetAuditFailure(combined))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(
            "> **NuGet audit:** failures may be `NU1904`/`NU1903` treated as errors. "
            + "Adjust `NuGetAuditMode` in `Directory.Build.props` or upgrade vulnerable packages. "
            + "This tool already re-ran `dotnet restore` and `build -v:normal` when minimal output had no NU lines.");
    }

    private static List<DotNetBuildDiagnosticParser.DiagnosticEntry> DeduplicateDiagnostics(
        IEnumerable<DotNetBuildDiagnosticParser.DiagnosticEntry> entries) =>
        entries
            .GroupBy(d => (d.Code, d.Location, d.Message))
            .Select(g => g.First())
            .ToList();

    private static List<DotNetBuildDiagnosticParser.DiagnosticEntry> MergeErrorEntries(
        string workDir,
        string combined,
        string runMetadata,
        IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> parsed)
    {
        var errors = parsed
            .Where(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dotnetVersion = SdkMismatchDiagnostics.TryParseDotNetVersionFromMetadata(runMetadata);
        foreach (var synthetic in SdkMismatchDiagnostics.CreateErrors(workDir, combined, dotnetVersion))
        {
            if (errors.All(e => !string.Equals(e.Code, synthetic.Code, StringComparison.Ordinal)
                                || e.Message != synthetic.Message))
            {
                errors.Insert(0, synthetic);
            }
        }

        return errors;
    }

    private static int CountLikelyIssueLines(string combined) =>
        combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("warning", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("FAILED", StringComparison.OrdinalIgnoreCase));

    private static void AppendRunMetadata(StringBuilder sb, string runMetadata, IReadOnlyList<string> stepsExecuted)
    {
        sb.AppendLine("### dotnet run");
        foreach (var line in runMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine(line);
        }

        if (stepsExecuted.Count > 0)
        {
            sb.AppendLine($"- **Steps:** {string.Join(" → ", stepsExecuted)}");
        }

        sb.AppendLine();
    }
}
