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
        "Runs dotnet run --project <csproj>. Executes a process. Prefer this over execute_dotnet_command.")]
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
