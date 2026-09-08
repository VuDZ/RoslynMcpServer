using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class TestTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<TestTools> _logger;

    public TestTools(SolutionManager solutionManager, ILogger<TestTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_test", Title = "Run dotnet test")]
    [Description(
        "Runs dotnet test on a project, solution, or test directory. Executes a process. "
        + "Prefer run_specific_test for one class or method. Omit configuration/platform to inherit load_workspace.")]
    public Task<string> RunDotNetTest(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory. Directories are allowed unlike run_dotnet_build.")]
        string workspacePath,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description("Skip rebuild. Use after a successful run_dotnet_build.")]
        bool noBuild = false,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDotnetTestAsync(
            nameof(RunDotNetTest),
            workspacePath,
            filter: null,
            filterDescription: null,
            requireFilterMatch: false,
            timeoutSeconds,
            noBuild,
            noRestore,
            configuration,
            platform,
            cancellationToken);
    }

    [McpServerTool(Name = "run_specific_test", Title = "Run a filtered dotnet test")]
    [Description(
        "Runs dotnet test filtered to one class and/or method. Executes a process. "
        + "Builds a VSTest-safe filter internally; do not use execute_dotnet_command.")]
    public async Task<string> RunSpecificTest(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory.")]
        string workspacePath,
        [Description("Test class name, simple or fully qualified.")]
        string? className = null,
        [Description("Test method name.")]
        string? methodName = null,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description("Skip rebuild. Use after a successful run_dotnet_build.")]
        bool noBuild = false,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunSpecificTest);

        try
        {
            if (string.IsNullOrWhiteSpace(className) && string.IsNullOrWhiteSpace(methodName))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: provide at least one of `className` or `methodName`.");
            }

            var solution = await _solutionManager.GetCurrentSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            var (filter, description) = await TestFilterHelper.BuildFilterAsync(
                solution, className, methodName, cancellationToken).ConfigureAwait(false);

            return await ExecuteDotnetTestAsync(
                    toolName,
                    workspacePath,
                    filter,
                    description,
                    requireFilterMatch: true,
                    timeoutSeconds,
                    noBuild,
                    noRestore,
                    configuration,
                    platform,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                "`run_specific_test` was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunSpecificTest failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to run specific test: {ex.Message}");
        }
    }

    [McpServerTool(Name = "get_test_list", Title = "List tests in workspace")]
    [Description(
        "Returns JSON list of test methods from the loaded workspace. Requires load_workspace.")]
    public async Task<string> GetTestList(
        [Description("Maximum tests to return.")] int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetTestList);
        try
        {
            var solution = await _solutionManager.GetCurrentSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("No workspace loaded."));
            }

            var json = await TestDiscoveryHelper.ListTestsJsonAsync(solution, maxResults, cancellationToken)
                .ConfigureAwait(false);
            var loadedPath = _solutionManager.GetLoadedWorkspacePath();
            if (IsEmptyTestListPayload(json))
            {
                var guidance = WorkspaceLoadGuidance.FormatEmptyTestListMessage(
                    loadedPath,
                    solution.ProjectIds.Count);
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    guidance + Environment.NewLine + Environment.NewLine + "```json\n" + json + "\n```");
            }

            var header = string.IsNullOrWhiteSpace(loadedPath)
                ? null
                : $"Loaded workspace: `{loadedPath}` ({solution.ProjectIds.Count} projects).";
            var body = header is null
                ? "```json\n" + json + "\n```"
                : header + Environment.NewLine + Environment.NewLine + "```json\n" + json + "\n```";
            return ToolTelemetry.TraceAndReturn(toolName, body);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`get_test_list` was cancelled by the MCP host.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTestList failed");
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private static bool IsEmptyTestListPayload(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("count", out var count)
                   && count.ValueKind == System.Text.Json.JsonValueKind.Number
                   && count.GetInt32() == 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    [McpServerTool(Name = "generate_test_method_stub", Title = "Generate test method stub")]
    [Description("Inserts a test method stub into a test class. Writes the file. Requires load_workspace.")]
    public async Task<string> GenerateTestMethodStub(
        [Description("Path to the test .cs file.")] string filePath,
        [Description("Test class that will receive the stub.")] string className,
        [Description("New test method name.")] string methodName,
        [Description("xunit (default), nunit, or mstest.")] string? testFramework = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GenerateTestMethodStub);
        try
        {
            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Document not in workspace: `{fullPath}`.");
            }

            var baseSolution = document.Project.Solution;
            var newDocument = await TestDiscoveryHelper.GenerateTestMethodStubAsync(
                document, className, methodName, testFramework, cancellationToken).ConfigureAwait(false);
            var written = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newDocument.Project.Solution, cancellationToken).ConfigureAwait(false);

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Added test stub `{methodName}` to `{className}`. Files touched: {written.Count}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateTestMethodStub failed");
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private async Task<string> ExecuteDotnetTestAsync(
        string toolName,
        string workspacePath,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch,
        int timeoutSeconds,
        bool noBuild,
        bool noRestore,
        string? configuration,
        string? platform,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Path not found: `{fullPath}`");
            }

            if (File.Exists(fullPath))
            {
                var ext = Path.GetExtension(fullPath);
                if (!string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".sln", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"When passing a file, it must be a `.csproj`, `.sln`, or `.slnx`: `{fullPath}`");
                }
            }

            var workDir = File.Exists(fullPath)
                ? WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath)
                : WorkspaceRootResolver.FindDirectoryContainingGlobalJson(fullPath) ?? fullPath;

            var targetPath = File.Exists(fullPath)
                ? fullPath
                : WorkspaceRootResolver.FindSolutionOrProjectInDirectory(fullPath) ?? fullPath;

            DotNetTestArguments.CliPlan plan;
            string? effectiveConfiguration;
            string? effectivePlatform;
            try
            {
                effectiveConfiguration = DotNetConfigurationArguments.Coalesce(
                    configuration, _solutionManager.LoadedConfiguration, nameof(configuration));
                effectivePlatform = DotNetConfigurationArguments.CoalescePlatform(
                    platform, _solutionManager.LoadedPlatform);
                plan = DotNetTestArguments.BuildPlan(
                    targetPath, filter, noBuild, noRestore, effectiveConfiguration, effectivePlatform);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            var extraMeta =
                $"- **Configuration:** {(string.IsNullOrWhiteSpace(effectiveConfiguration) ? "(SDK/solution default)" : effectiveConfiguration)}"
                + Environment.NewLine
                + $"- **Platform:** {(string.IsNullOrWhiteSpace(effectivePlatform) ? "(SDK/solution default)" : effectivePlatform)}"
                + Environment.NewLine
                + $"- **PreTestBuild:** {(plan.IncludesPreTestBuild ? "yes (`dotnet build` then `dotnet test --no-build`)" : "skipped (`noBuild=true`)")}";

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (plan.PreTestBuildArguments is not null)
            {
                var buildRun = await DotNetCliRunner.RunWithMetadataAsync(
                    plan.PreTestBuildArguments,
                    workDir,
                    cancellationToken,
                    timeout).ConfigureAwait(false);

                if (buildRun.TimedOut)
                {
                    var timedOut = new StringBuilder();
                    timedOut.AppendLine("## Pre-test build timed out");
                    timedOut.AppendLine();
                    timedOut.AppendLine("Tests were not started because `dotnet build` exceeded the tool timeout.");
                    timedOut.AppendLine();
                    timedOut.AppendLine(buildRun.RunMetadata);
                    timedOut.AppendLine(extraMeta);
                    timedOut.AppendLine();
                    timedOut.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
                    timedOut.AppendLine();
                    TruncatedProcessLog.AppendLastCharacters(
                        timedOut, "Console output before kill:", buildRun.CombinedOutput);
                    return ToolTelemetry.TraceAndReturn(toolName, timedOut.ToString().TrimEnd());
                }

                if (buildRun.ExitCode != 0)
                {
                    var failed = new StringBuilder();
                    failed.AppendLine("## Pre-test build failed");
                    failed.AppendLine();
                    failed.AppendLine("Tests were not started because `dotnet build` failed.");
                    failed.AppendLine();
                    failed.AppendLine(buildRun.RunMetadata);
                    failed.AppendLine(extraMeta);
                    TruncatedProcessLog.AppendLastCharacters(
                        failed,
                        TruncatedProcessLog.BuildPreambleBuildConsoleTail(buildRun.ExitCode),
                        buildRun.CombinedOutput);
                    return ToolTelemetry.TraceAndReturn(toolName, failed.ToString().TrimEnd());
                }

                timeout = DotNetTestArguments.RemainingTimeout(timeout, sw.Elapsed);
                if (timeout == TimeSpan.Zero)
                {
                    var exhausted = new StringBuilder();
                    exhausted.AppendLine("## Test run timed out");
                    exhausted.AppendLine();
                    exhausted.AppendLine(
                        "Pre-test `dotnet build` succeeded, but no time remained in `timeoutSeconds` to start `dotnet test`.");
                    exhausted.AppendLine();
                    exhausted.AppendLine(buildRun.RunMetadata);
                    exhausted.AppendLine(extraMeta);
                    exhausted.AppendLine();
                    exhausted.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
                    return ToolTelemetry.TraceAndReturn(toolName, exhausted.ToString().TrimEnd());
                }
            }

            var run = await DotNetCliRunner.RunWithMetadataAsync(
                plan.TestArguments,
                workDir,
                cancellationToken,
                timeout).ConfigureAwait(false);

            if (run.TimedOut)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Test run timed out");
                sb.AppendLine();
                sb.AppendLine(run.RunMetadata);
                sb.AppendLine(extraMeta);
                sb.AppendLine();
                sb.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
                sb.AppendLine();
                TruncatedProcessLog.AppendLastCharacters(sb, "Console output before kill:", run.CombinedOutput);
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            var parse = VstestOutputParser.Parse(run.CombinedOutput, run.ExitCode);
            var markdown = VstestOutputParser.BuildMarkdownReport(
                parse,
                run.ExitCode,
                run.CombinedOutput,
                filter,
                filterDescription,
                requireFilterMatch);

            if (requireFilterMatch
                && markdown.Contains("## Filtered test run — no matching tests", StringComparison.Ordinal))
            {
                var agentHint = WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(
                    _solutionManager.GetLoadedWorkspacePath(),
                    filterDescription,
                    targetPath);
                markdown = markdown + Environment.NewLine + Environment.NewLine + agentHint;
            }

            if (run.ExitCode != 0 && LooksLikeSilentFailure(run.CombinedOutput, parse))
            {
                var sb = new StringBuilder();
                sb.AppendLine(markdown);
                sb.AppendLine();
                sb.AppendLine("### Silent / unparsed failure hints");
                sb.AppendLine(
                    "Exit code ≠ 0 but no clear VSTest summary or MSBuild/NU diagnostics were parsed "
                    + "(common after hung restore or locked `obj`).");
                sb.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: false));
                sb.AppendLine();
                sb.AppendLine(run.RunMetadata);
                sb.AppendLine(extraMeta);
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                markdown + Environment.NewLine + Environment.NewLine + run.RunMetadata
                + Environment.NewLine + extraMeta);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                "`dotnet test` was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ToolName} failed for {WorkspacePath}", toolName, workspacePath);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Failed to run `dotnet test`: {ex.Message}");
        }
    }

    private static bool LooksLikeSilentFailure(string combinedOutput, VstestOutputParser.ParseResult parse)
    {
        if (parse.Summary is not null || parse.HasRecognizedSummary || parse.Failures.Count > 0)
        {
            return false;
        }

        return combinedOutput.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
               || combinedOutput.Contains("Restore target(s)", StringComparison.OrdinalIgnoreCase)
               || combinedOutput.Contains("0 Error(s)", StringComparison.OrdinalIgnoreCase)
               || string.IsNullOrWhiteSpace(combinedOutput);
    }
}
