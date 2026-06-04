using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class BuildTools
{
    private const int MaxDiagnostics = 20;

    private static readonly Regex MsBuildDiagnosticLine = new(
        pattern: @"^(?<loc>.+\(\d+,\s*\d+\))\s*:\s*(?<sev>error|warning)\s+(?<code>\S+)\s*:\s*(?<msg>.*)$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            var run = await DotNetCliRunner.RunWithMetadataAsync(
                $"build \"{fullPath}\"",
                workDir,
                cancellationToken).ConfigureAwait(false);
            var combined = run.CombinedOutput;
            var processExitCode = run.ExitCode;
            var diagnostics = ParseMsBuildDiagnostics(combined);
            var errorEntries = diagnostics
                .Where(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var warningEntries = diagnostics
                .Where(d => string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var display = new List<DiagnosticEntry>();
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
                var sb = new StringBuilder();
                sb.AppendLine("## Build succeeded");
                sb.AppendLine();
                AppendRunMetadata(sb, run.RunMetadata);
                sb.AppendLine("No compiler **errors** reported (MSBuild `file(line,col): error ...` pattern).");
                if (warningEntries.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var d in warningEntries.Take(MaxDiagnostics))
                    {
                        sb.AppendLine($"- **warning** `{d.Code}` `{d.Location}` — {d.Message}");
                    }

                    if (warningEntries.Count > MaxDiagnostics)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[!] More than 20 warnings matched; only the first 20 are shown.");
                    }
                }

                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), sb.ToString().TrimEnd());
            }

            if (errorEntries.Count == 0 && processExitCode != 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Build failed");
                sb.AppendLine();
                AppendRunMetadata(sb, run.RunMetadata);
                sb.AppendLine(
                    $"Exit code: `{processExitCode}`. No lines matched the standard MSBuild diagnostic pattern `path(line,col): error|warning CODE: message`.");
                TruncatedProcessLog.AppendLastCharacters(
                    sb,
                    TruncatedProcessLog.BuildPreambleBuildConsoleTail(processExitCode),
                    combined);
                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), sb.ToString().TrimEnd());
            }

            var errSb = new StringBuilder();
            errSb.AppendLine("## Build failed");
            errSb.AppendLine();
            AppendRunMetadata(errSb, run.RunMetadata);
            errSb.AppendLine("Diagnostics:");
            foreach (var d in display)
            {
                errSb.AppendLine($"- **{d.Severity}** `{d.Code}` `{d.Location}` — {d.Message}");
            }

            if (truncated)
            {
                errSb.AppendLine();
                errSb.AppendLine("[!] More than 20 diagnostics reported. Showing the first 20 (errors first) to protect LLM context.");
            }

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

    private static void AppendRunMetadata(StringBuilder sb, string runMetadata)
    {
        sb.AppendLine("### dotnet run");
        foreach (var line in runMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
    }

    private static List<DiagnosticEntry> ParseMsBuildDiagnostics(string combinedOutput)
    {
        var list = new List<DiagnosticEntry>();
        foreach (var raw in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd();
            var m = MsBuildDiagnosticLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            list.Add(new DiagnosticEntry(
                m.Groups["sev"].Value,
                m.Groups["code"].Value,
                m.Groups["loc"].Value.Trim(),
                m.Groups["msg"].Value.Trim()));
        }

        return list;
    }

    private sealed record DiagnosticEntry(string Severity, string Code, string Location, string Message);
}
