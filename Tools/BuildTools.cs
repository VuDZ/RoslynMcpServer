using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class BuildTools
{
    public BuildTools(SolutionManager solutionManager, ILogger<BuildTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_build", Title = "Run dotnet build")]
    [Description(
        "Runs dotnet build with parsed diagnostics. Executes a process. workspacePath must be a .csproj/.sln/.slnx file, not a directory. "
        + "Default noIncremental=true. Omit configuration/platform to inherit load_workspace. Extra `dotnet build` args inherit from load_workspace `buildArgs`. "
        + "Optional projectName builds that project via its .sln/.slnx MSBuild target (solution-folder path).")]
    public async Task<string> RunDotNetBuild(
        [Description("Path to a .csproj, .sln, or .slnx file, not a directory.")]
        string workspacePath,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("When true (default), pass --no-incremental so up-to-date cache cannot hide errors.")]
        bool noIncremental = true,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        [Description(
            "Optional project name. When set, workspacePath must be a .sln/.slnx. Builds that project via its solution-folder MSBuild target instead of the whole solution.")]
        string? projectName = null,
        [Description(DiagnosticReportAttachment.ReportCursorParameterDescription)]
        string? reportCursor = null,
        [Description(BuildOutputReport.WarningsParameterDescription)]
        bool includeBuildWarnings = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(reportCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(RunDotNetBuild),
                    DiagnosticReportAttachment.FormatChunkResponse(
                        DiagnosticReportStore.TryTakeChunk(reportCursor)));
            }

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
                && !string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(RunDotNetBuild),
                    $"Path must be a .csproj, .sln, or .slnx file: `{fullPath}`");
            }

            var workDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath);
            string? effectiveConfiguration;
            string? effectivePlatform;
            try
            {
                effectiveConfiguration = DotNetConfigurationArguments.Coalesce(
                    configuration, _solutionManager.LoadedConfiguration, nameof(configuration));
                effectivePlatform = DotNetConfigurationArguments.CoalescePlatformForTarget(
                    platform,
                    _solutionManager.LoadedPlatformRaw,
                    _solutionManager.LoadedPlatform,
                    fullPath);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunDotNetBuild), $"Error: {ex.Message}");
            }

            string? solutionTarget = null;
            string? resolvedProjectName = null;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    return ToolTelemetry.TraceAndReturn(
                        nameof(RunDotNetBuild),
                        "Error: `projectName` requires `workspacePath` to be a `.sln` or `.slnx` so the project can be built as a solution target.");
                }

                var resolved = SolutionProjectTargetResolver.TryResolve(fullPath, projectName);
                if (!resolved.Success)
                {
                    return ToolTelemetry.TraceAndReturn(
                        nameof(RunDotNetBuild),
                        resolved.ErrorMessage ?? "Error: could not resolve `projectName`.");
                }

                solutionTarget = resolved.TargetName;
                resolvedProjectName = resolved.DisplayName;
            }

            var probe = await DotNetBuildProbe.RunAsync(
                    fullPath,
                    workDir,
                    cancellationToken,
                    configuration: effectiveConfiguration,
                    noIncremental: noIncremental,
                    platform: effectivePlatform,
                    buildArgs: _solutionManager.LoadedBuildArgs,
                    target: solutionTarget,
                    progress: McpToolProgressReporter.TryCreate(progress))
                .ConfigureAwait(false);
            var context = new StringBuilder();
            AppendRunMetadata(context, probe.RunMetadata, probe.StepsExecuted, effectiveConfiguration,
                effectivePlatform, probe.NoIncremental, _solutionManager.LoadedBuildArgs,
                resolvedProjectName, solutionTarget);
            var additionalErrors = SdkMismatchDiagnostics.CreateErrors(
                workDir, probe.CombinedOutput,
                SdkMismatchDiagnostics.TryParseDotNetVersionFromMetadata(probe.RunMetadata));
            return ToolTelemetry.TraceAndReturn(
                nameof(RunDotNetBuild),
                BuildOutputReport.Format(
                    probe.CombinedOutput,
                    probe.ExitCode,
                    context.ToString().TrimEnd(),
                    includeBuildWarnings,
                    timedOut: probe.TimedOut,
                    budgetExhausted: probe.BudgetExhausted,
                    additionalErrors: additionalErrors));
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(RunDotNetBuild),
                "Build was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunDotNetBuild failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(RunDotNetBuild),
                $"Failed to run `dotnet build`: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendRunMetadata(
        StringBuilder sb,
        string runMetadata,
        IReadOnlyList<string> stepsExecuted,
        string? configuration,
        string? platform,
        bool noIncremental,
        string? buildArgs,
        string? projectName,
        string? solutionTarget)
    {
        foreach (var line in runMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine(line);
        }

        sb.AppendLine(
            $"- **Configuration:** {(string.IsNullOrWhiteSpace(configuration) ? "(SDK/solution default)" : configuration)}");
        sb.AppendLine(
            $"- **Platform:** {(string.IsNullOrWhiteSpace(platform) ? "(SDK/solution default)" : platform)}");
        sb.AppendLine(
            $"- **ProjectName:** {(string.IsNullOrWhiteSpace(projectName) ? "(none)" : projectName)}");
        sb.AppendLine(
            $"- **SolutionTarget:** {(string.IsNullOrWhiteSpace(solutionTarget) ? "(none)" : $"`{solutionTarget}` (`-t`)")}");
        sb.AppendLine($"- **BuildArgs:** {DotNetBuildArguments.FormatMetadata(buildArgs)}");
        sb.AppendLine($"- **NoIncremental:** `{noIncremental}`{(noIncremental ? " (`--no-incremental`)" : " (MSBuild up-to-date allowed)")}");

        if (stepsExecuted.Count > 0)
        {
            sb.AppendLine($"- **Steps:** {string.Join(" → ", stepsExecuted)}");
        }

        sb.AppendLine();
    }

    private readonly SolutionManager _solutionManager;
    private readonly ILogger<BuildTools> _logger;
}
