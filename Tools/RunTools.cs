using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class RunTools
{
    private readonly ILogger<RunTools> _logger;

    public RunTools(ILogger<RunTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_run", Title = "Run dotnet run")]
    [Description(
        "Runs `dotnet run --project <csproj>` with pinned SDK (same as run_dotnet_build). "
        + "Default timeout **120s** (raise `timeoutSeconds` for long jobs; `0` = no timeout). "
        + "Returns separate stdout/stderr with size limits — use for console apps (progress on stderr). "
        + "Do not use raw shell `dotnet run` or execute_dotnet_command for project runs.")]
    public async Task<string> RunDotNetRun(
        [Description("Path to a .csproj (executable/worker project). Same roots as load_workspace / run_dotnet_build.")]
        string workspacePath,
        [Description("Optional arguments after `--` (space-separated), e.g. `https://server/tfs/.../100` `--verbose`.")]
        string? arguments = null,
        [Description("Optional working directory override. When omitted, uses global.json repo root.")]
        string? workingDirectory = null,
        [Description("Process timeout in seconds. Default 120. 0 = no timeout.")]
        int timeoutSeconds = 120,
        [Description("Max stdout characters in the response (head+tail when truncated). Default 8000.")]
        int maxStdoutChars = ProcessOutputExcerpt.DefaultMaxStdoutCharacters,
        [Description("Max stderr characters; prefers tail (progress bar). Default 2000.")]
        int maxStderrChars = ProcessOutputExcerpt.DefaultMaxStderrCharacters,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunDotNetRun);

        try
        {
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

            var args = new StringBuilder("run --project \"");
            args.Append(fullPath);
            args.Append('"');

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                args.Append(" -- ");
                args.Append(arguments.Trim());
            }

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var run = await DotNetCliRunner.RunSeparatedAsync(args.ToString(), workDir, timeout, cancellationToken)
                .ConfigureAwait(false);

            var stdoutExcerpt = ProcessOutputExcerpt.BuildStdoutExcerpt(run.StdOut, maxStdoutChars);
            var stderrExcerpt = ProcessOutputExcerpt.BuildStderrExcerpt(run.StdErr, maxStderrChars);

            var sb = new StringBuilder();
            sb.AppendLine(run.ExitCode == 0 && !run.TimedOut ? "## dotnet run succeeded" : "## dotnet run finished");
            sb.AppendLine();
            foreach (var line in run.RunMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine(line);
            }

            sb.AppendLine($"- **Command:** `dotnet {args}`");
            sb.AppendLine($"- **Exit code:** `{run.ExitCode}`");
            sb.AppendLine($"- **Timed out:** {(run.TimedOut ? "yes" : "no")}");
            if (!string.IsNullOrEmpty(run.ExceptionType))
            {
                sb.AppendLine($"- **Exception:** `{run.ExceptionType}`");
            }

            sb.AppendLine($"- **Stdout length:** {run.StdOut.Length} chars (excerpt below)");
            sb.AppendLine($"- **Stderr length:** {run.StdErr.Length} chars (excerpt below)");
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

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
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
}
