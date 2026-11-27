using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Tools;

public sealed class BuildTools
{
    // Task.Delay accepts at most uint.MaxValue - 1 milliseconds.
    private const int MaxTimeoutSeconds = (int)((uint.MaxValue - 1) / 1000);

    public BuildTools(SolutionManager solutionManager, ILogger<BuildTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_build", Title = "Run dotnet build")]
    [Description(
        "Runs dotnet build with parsed diagnostics. Inherits configuration, platform, and buildArgs from load_workspace. "
        + "A direct .csproj target requires require-solution=false in RoslynMcp.jsonc (the default).")]
    public async Task<string> RunDotNetBuild(
        [Description("Build file (.csproj, .sln, .slnx, or .slnf).")]
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
        [Description("Build/restore budget in seconds. Default: 180 per step, 300 total. 0 disables the limit.")]
        int? timeoutSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string? fullPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(reportCursor))
            {
                var chunk = DiagnosticReportStore.TryTakeChunk(reportCursor);
                return ToolTelemetry.TraceAndReturn(
                    nameof(RunDotNetBuild),
                    DiagnosticReportAttachment.FormatChunkResponse(chunk),
                    success: chunk.Ok);
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return Complete("Error: `workspacePath` is empty.");
            }

            fullPath = Path.GetFullPath(workspacePath);
            var inputKind = WorkspaceInputPath.Classify(fullPath);
            if (_solutionManager.FileSettings.RequireSolution && inputKind == WorkspaceInputKind.CSharpProject)
            {
                return Complete("Error: " + WorkspaceEntryPolicy.Rejection(_solutionManager.FileSettings));
            }

            if (timeoutSeconds is < 0 or > MaxTimeoutSeconds)
            {
                return Complete($"Error: `timeoutSeconds` must be between 0 and {MaxTimeoutSeconds}.");
            }

            if (!File.Exists(fullPath))
            {
                return Complete($"File not found: `{fullPath}`");
            }

            if (inputKind == WorkspaceInputKind.Unknown)
            {
                return Complete($"Path must be a .csproj, .sln, .slnx, or .slnf file: `{fullPath}`");
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
                return Complete($"Error: {ex.Message}");
            }

            string? solutionTarget = null;
            string? resolvedProjectName = null;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var resolved = SolutionProjectTargetResolver.TryResolve(fullPath, projectName);
                if (!resolved.Success)
                {
                    return Complete(resolved.ErrorMessage ?? "Error: could not resolve `projectName`.");
                }

                solutionTarget = resolved.TargetName;
                resolvedProjectName = resolved.DisplayName;
            }

            if (SolutionFilterPath.FindMissingBackingSolutionError(fullPath) is { } filterError)
            {
                return Complete(filterError);
            }

            TimeSpan? budget = timeoutSeconds switch
            {
                null => null,
                0 => Timeout.InfiniteTimeSpan,
                int seconds => TimeSpan.FromSeconds(seconds),
            };
            var probe = await DotNetBuildProbe.RunAsync(
                    fullPath,
                    workDir,
                    cancellationToken,
                    overallBudget: budget,
                    stepTimeout: budget,
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
            var additionalErrors = SdkMismatchDiagnostics.CreateErrors(probe.SdkSelection, probe.CombinedOutput);
            var report = BuildOutputReport.Format(
                probe.CombinedOutput,
                probe.ExitCode,
                context.ToString().TrimEnd(),
                includeBuildWarnings,
                out var succeeded,
                timedOut: probe.TimedOut,
                budgetExhausted: probe.BudgetExhausted,
                additionalErrors: additionalErrors);
            return Complete(report, success: succeeded);
        }
        catch (OperationCanceledException)
        {
            return Complete("Build was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunDotNetBuild failed for {WorkspacePath}", workspacePath);
            return Complete($"Failed to run `dotnet build`: {ex.GetType().Name}: {ex.Message}");
        }

        string Complete(string result, bool success = false) => ToolTelemetry.TraceAndReturn(
            nameof(RunDotNetBuild),
            cancellationToken.IsCancellationRequested
                ? result
                : WorkspaceEntryPolicy.AppendWarning(result, fullPath, _solutionManager.FileSettings),
            success: success);
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
