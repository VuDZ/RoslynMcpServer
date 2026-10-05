using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class RunTools
{
    public RunTools(ILogger<RunTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_run", Title = "Run dotnet run")]
    [Description(
        "Builds then runs an executable csproj within one timeout. Preserves application output. Context on failure only.")]
    public async Task<string> RunDotNetRun(
        [Description("Path to an executable .csproj.")]
        string workspacePath,
        [Description("Arguments after --.")]
        string? arguments = null,
        [Description("Working directory override. Omit to use global.json repo root.")]
        string? workingDirectory = null,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = 120,
        [Description("Max stdout characters in the response.")]
        int maxStdoutChars = ProcessOutputExcerpt.DefaultMaxStdoutCharacters,
        [Description("Max stderr characters in the response.")]
        int maxStderrChars = ProcessOutputExcerpt.DefaultMaxStderrCharacters,
        [Description(DiagnosticReportAttachment.ReportCursorParameterDescription)]
        string? reportCursor = null,
        [Description(BuildOutputReport.WarningsParameterDescription)]
        bool includeBuildWarnings = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunDotNetRun);

        try
        {
            if (!string.IsNullOrWhiteSpace(reportCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    DiagnosticReportAttachment.FormatChunkResponse(
                        DiagnosticReportStore.TryTakeChunk(reportCursor)));
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: Project file not found: `{fullPath}`");
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` must be a .csproj file.");
            }

            var workDir = string.IsNullOrWhiteSpace(workingDirectory)
                ? WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath)
                : Path.GetFullPath(workingDirectory);

            var args = new StringBuilder("run --no-build --project \"");
            args.Append(fullPath);
            args.Append('"');

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                args.Append(" -- ");
                args.Append(arguments.Trim());
            }

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var cliProgress = McpToolProgressReporter.TryCreate(progress);
            var buildArguments = DotNetTestArguments.BuildPreTestBuild(fullPath);
            var build = await CliProgressStep.RunWithMetadataAsync(
                    buildArguments, workDir, cancellationToken, timeout, cliProgress, CliProgressStep.BuildStage)
                .ConfigureAwait(false);
            if (build.TimedOut || build.ExitCode != 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Application was not started because the build did not succeed."
                    + Environment.NewLine + Environment.NewLine
                    + BuildOutputReport.Format(
                        build.CombinedOutput,
                        build.ExitCode,
                        build.RunMetadata + Environment.NewLine + $"- **Command:** `dotnet {buildArguments}`",
                        includeBuildWarnings,
                        timedOut: build.TimedOut));
            }

            timeout = DotNetTestArguments.RemainingTimeout(timeout, watch.Elapsed);
            if (timeout == TimeSpan.Zero)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "## dotnet run timed out" + Environment.NewLine
                    + "Build succeeded, but no time remained to start the application."
                    + Environment.NewLine + "### Execution context" + Environment.NewLine + build.RunMetadata);
            }

            var run = await CliProgressStep.RunSeparatedAsync(
                    args.ToString(),
                    workDir,
                    timeout,
                    cancellationToken,
                    cliProgress,
                    CliProgressStep.RunStage)
                .ConfigureAwait(false);

            var stdoutExcerpt = ProcessOutputExcerpt.BuildStdoutExcerpt(run.StdOut, maxStdoutChars);
            var stderrExcerpt = ProcessOutputExcerpt.BuildStderrExcerpt(run.StdErr, maxStderrChars);

            var sb = new StringBuilder();
            sb.AppendLine(run.TimedOut ? "## dotnet run timed out"
                : run.ExitCode == 0 && run.ExceptionType is null ? "## dotnet run succeeded" : "## dotnet run failed");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(stdoutExcerpt))
            {
                sb.AppendLine("### StdOut");
                sb.AppendLine("```text");
                sb.AppendLine(stdoutExcerpt);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("### StdOut");
                sb.AppendLine("(empty)");
            }

            sb.AppendLine();
            if (!string.IsNullOrEmpty(stderrExcerpt))
            {
                sb.AppendLine("### StdErr");
                sb.AppendLine("```text");
                sb.AppendLine(stderrExcerpt);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("### StdErr");
                sb.AppendLine("(empty)");
            }

            if (run.ExitCode != 0 && !run.TimedOut)
            {
                sb.AppendLine();
                sb.AppendLine(
                    "> Check stderr tail for progress/errors. For HTTP/proxy issues verify corporate network — not an MCP SDK mismatch.");
            }

            if (includeBuildWarnings)
            {
                var warnings = BuildOutputReport.FormatWarnings(build.CombinedOutput);
                if (warnings.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(warnings);
                }
            }

            if (run.ExitCode != 0 || run.TimedOut || run.ExceptionType is not null)
            {
                sb.AppendLine();
                sb.AppendLine("### Execution context");
                sb.AppendLine(run.RunMetadata);
                sb.AppendLine($"- **Command:** `dotnet {args}`");
                sb.AppendLine($"- **Exit code:** `{run.ExitCode}`");
                sb.AppendLine($"- **Timed out:** {(run.TimedOut ? "yes" : "no")}");
                if (run.ExceptionType is not null)
                {
                    sb.AppendLine($"- **Exception:** `{run.ExceptionType}`");
                }
            }

            var text = sb.ToString().TrimEnd();
            var combined = string.IsNullOrEmpty(run.StdErr)
                ? run.StdOut
                : string.IsNullOrEmpty(run.StdOut)
                    ? run.StdErr
                    : run.StdOut + "\n" + run.StdErr;
            var shouldStore = DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(text)
                              || run.StdOut.Length > maxStdoutChars
                              || run.StdErr.Length > maxStderrChars;
            text = DiagnosticReportAttachment.AttachToResponse(
                text, build.CombinedOutput + Environment.NewLine + combined, shouldStore);
            return ToolTelemetry.TraceAndReturn(toolName, text);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`run_dotnet_run` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunDotNetRun failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to run project: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private readonly ILogger<RunTools> _logger;
}
