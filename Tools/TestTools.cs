using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class TestTools
{
    private const string IncludeFullOutputDescription =
        "When true, include failed-test Standard Output/Error up to 100000 chars per stream. Default false uses maxOutputChars (2500 head+tail).";

    private const string MaxOutputCharsDescription =
        "Per-failure Standard Output budget in characters. 0 = 2500 (head 1600 + tail 700). Capped at 100000. With includeFullOutput, 0 means the 100000 cap. StdErr scales with this.";

    private const string DllRouteSentence =
        " With `binariesPath`, `buildPolicy` selects a solution-target build, then `dotnet test --no-build` on that DLL.";

    private const string NoBuildOmitFalseDescription =
        "Skip the pre-test build. Omit means false. On the DLL route, false means `always` and true means `never`; do not pass this together with `buildPolicy`.";

    private const string NoBuildOmitTrueDescription =
        "Skip the pre-test build. Omit means true. On the DLL route, false means `always` and true means `never`; do not pass this together with `buildPolicy`.";

    private const string BuildPolicyDescription =
        "`auto`, `always`, or `never`. Requires `binariesPath`. When both this and `noBuild` are omitted, the DLL route uses `auto`. `never` runs tests only if that build is current.";

    private const string BinariesPathDescription =
        "Directory containing AssemblyName.dll. Needs a loaded .sln/.slnx and a .csproj workspacePath. Pre-build uses the solution project target (`-t`), then tests that DLL with `--no-build`.";

    /// <summary>Historical omit of <c>noBuild</c> on the non-DLL route: rebuild.</summary>
    private const bool DotNetTestOmitSkipsRebuild = false;

    /// <summary>Historical omit of <c>noBuild</c> on the non-DLL route: rebuild.</summary>
    private const bool SpecificTestOmitSkipsRebuild = false;

    /// <summary>Historical omit of <c>noBuild</c> on the non-DLL route: skip the rebuild.</summary>
    private const bool FilterTestOmitSkipsRebuild = true;

    private readonly SolutionManager _solutionManager;
    private readonly ILogger<TestTools> _logger;
    private readonly TestDllEnsure.Runner? _cliRunner;

    public TestTools(SolutionManager solutionManager, ILogger<TestTools> logger)
        : this(solutionManager, logger, cliRunner: null)
    {
    }

    internal TestTools(SolutionManager solutionManager, ILogger<TestTools> logger, TestDllEnsure.Runner? cliRunner)
    {
        _solutionManager = solutionManager;
        _logger = logger;
        _cliRunner = cliRunner;
    }

    [McpServerTool(Name = "run_dotnet_test", Title = "Run dotnet test")]
    [Description(
        "Runs dotnet test on a project, solution, or test directory. Executes a process. "
        + "Prefer run_specific_test for one class or method. Omit configuration/platform to inherit load_workspace. Pre-test `dotnet build` also inherits load_workspace `buildArgs`."
        + DllRouteSentence)]
    public Task<string> RunDotNetTest(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory. Directories are allowed unlike run_dotnet_build.")]
        string workspacePath,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description(NoBuildOmitFalseDescription)]
        bool? noBuild = null,
        [Description(BuildPolicyDescription)]
        string? buildPolicy = null,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        [Description(BinariesPathDescription)]
        string? binariesPath = null,
        [Description(IncludeFullOutputDescription)]
        bool includeFullOutput = false,
        [Description(MaxOutputCharsDescription)]
        int maxOutputChars = 0,
        [Description(DiagnosticReportAttachment.ReportCursorParameterDescription)]
        string? reportCursor = null,
        IProgress<ProgressNotificationValue>? progress = null,
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
            buildPolicy,
            DotNetTestOmitSkipsRebuild,
            noRestore,
            configuration,
            platform,
            binariesPath,
            includeFullOutput,
            maxOutputChars,
            reportCursor,
            progress,
            cancellationToken);
    }

    [McpServerTool(Name = "run_specific_test", Title = "Run a filtered dotnet test")]
    [Description(
        "Runs dotnet test filtered to one class and/or method. Executes a process. "
        + "Builds a VSTest-safe filter internally; do not use execute_dotnet_command."
        + DllRouteSentence)]
    public async Task<string> RunSpecificTest(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory.")]
        string workspacePath,
        [Description("Test class name, simple or fully qualified.")]
        string? className = null,
        [Description("Test method name.")]
        string? methodName = null,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description(NoBuildOmitFalseDescription)]
        bool? noBuild = null,
        [Description(BuildPolicyDescription)]
        string? buildPolicy = null,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        [Description(BinariesPathDescription)]
        string? binariesPath = null,
        [Description(IncludeFullOutputDescription)]
        bool includeFullOutput = false,
        [Description(MaxOutputCharsDescription)]
        int maxOutputChars = 0,
        [Description(DiagnosticReportAttachment.ReportCursorParameterDescription)]
        string? reportCursor = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunSpecificTest);

        try
        {
            if (!string.IsNullOrWhiteSpace(reportCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    DiagnosticReportAttachment.FormatChunkResponse(
                        DiagnosticReportStore.TryTakeChunk(reportCursor)));
            }

            if (string.IsNullOrWhiteSpace(className) && string.IsNullOrWhiteSpace(methodName))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: provide at least one of `className` or `methodName`.");
            }

            var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false);
            (string filter, string description) filterAndDescription;
            if (solution is null)
            {
                filterAndDescription = await TestFilterHelper.BuildFilterAsync(
                    solution: null, className, methodName, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                filterAndDescription = (await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                    sol => TestFilterHelper.BuildFilterAsync(sol, className, methodName, cancellationToken),
                    () => _solutionManager.GetSanitizedPublishedSolution(),
                    solution,
                    cancellationToken).ConfigureAwait(false)).Value;
            }

            var (filter, description) = filterAndDescription;

            var result = await ExecuteDotnetTestAsync(
                    toolName,
                    workspacePath,
                    filter,
                    description,
                    requireFilterMatch: true,
                    timeoutSeconds,
                    noBuild,
                    buildPolicy,
                    SpecificTestOmitSkipsRebuild,
                    noRestore,
                    configuration,
                    platform,
                    binariesPath,
                    includeFullOutput,
                    maxOutputChars,
                    reportCursor: null,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            return _solutionManager.WithDiskSyncNotes(result);
        }
        catch (OperationCanceledException)
        {
            return _solutionManager.WithDiskSyncNotes(ToolTelemetry.TraceAndReturn(
                toolName,
                "`run_specific_test` was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunSpecificTest failed for {WorkspacePath}", workspacePath);
            return _solutionManager.WithDiskSyncNotes(
                ToolTelemetry.TraceAndReturn(toolName, $"Failed to run specific test: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "run_test_by_filter", Title = "Run tests by VSTest filter")]
    [Description(
        "Runs dotnet test with a raw VSTest --filter. Executes a process. "
        + "Prefer run_specific_test for one class or method. Omit configuration/platform to inherit load_workspace."
        + DllRouteSentence)]
    public Task<string> RunTestByFilter(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory.")]
        string workspacePath,
        [Description("VSTest --filter string, passed through (e.g. FullyQualifiedName~MyClass).")]
        string filter,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description(NoBuildOmitTrueDescription)]
        bool? noBuild = null,
        [Description(BuildPolicyDescription)]
        string? buildPolicy = null,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        [Description(BinariesPathDescription)]
        string? binariesPath = null,
        [Description(IncludeFullOutputDescription)]
        bool includeFullOutput = false,
        [Description(MaxOutputCharsDescription)]
        int maxOutputChars = 0,
        [Description(DiagnosticReportAttachment.ReportCursorParameterDescription)]
        string? reportCursor = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(reportCursor))
        {
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(RunTestByFilter),
                    DiagnosticReportAttachment.FormatChunkResponse(
                        DiagnosticReportStore.TryTakeChunk(reportCursor))));
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(RunTestByFilter),
                    "Error: `filter` is empty."));
        }

        return ExecuteDotnetTestAsync(
            nameof(RunTestByFilter),
            workspacePath,
            filter.Trim(),
            "Caller-supplied VSTest filter",
            requireFilterMatch: false,
            timeoutSeconds,
            noBuild,
            buildPolicy,
            FilterTestOmitSkipsRebuild,
            noRestore,
            configuration,
            platform,
            binariesPath,
            includeFullOutput,
            maxOutputChars,
            reportCursor: null,
            progress,
            cancellationToken);
    }

    [McpServerTool(Name = "get_test_list", Title = "List tests in workspace")]
    [Description(
        "Returns JSON list of test methods from the loaded workspace. Requires load_workspace. "
        + "Optional projectName and nameContains filter before maxResults.")]
    public async Task<string> GetTestList(
        [Description("Maximum tests to return.")] int maxResults = 200,
        [Description("Limit discovery to this Roslyn project (name, file name, or assembly).")]
        string? projectName = null,
        [Description("Case-insensitive substring of the test FQN (namespace, class, method).")]
        string? nameContains = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetTestList);
        try
        {
            var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return Finish(_solutionManager.FormatNoPublishedSolutionMessage("No workspace loaded."));
            }

            var listed = await TestDiscoveryHelper.ListTestsJsonAsync(
                    solution, maxResults, projectName, nameContains, cancellationToken)
                .ConfigureAwait(false);
            if (!listed.Success)
            {
                return Finish(listed.Payload);
            }

            var json = listed.Payload;
            var loadedPath = _solutionManager.GetLoadedWorkspacePath();
            if (IsEmptyTestListPayload(json))
            {
                // Filters only explain an empty result when the workspace did find test methods.
                // With zero methods found, the filter advice is misleading — the workspace scope is
                // the real signal.
                var guidance = listed.FiltersApplied && listed.TotalTestMethodsFound > 0
                    ? WorkspaceLoadGuidance.FormatFilteredTestListEmptyMessage(
                        loadedPath, projectName, nameContains, listed.TotalTestMethodsFound)
                    : WorkspaceLoadGuidance.FormatEmptyTestListMessage(
                        loadedPath,
                        solution.ProjectIds.Count,
                        projectName,
                        nameContains);
                return Finish(guidance + Environment.NewLine + Environment.NewLine + "```json\n" + json + "\n```");
            }

            var header = string.IsNullOrWhiteSpace(loadedPath)
                ? null
                : $"Loaded workspace: `{loadedPath}` ({solution.ProjectIds.Count} projects).";
            var body = header is null
                ? "```json\n" + json + "\n```"
                : header + Environment.NewLine + Environment.NewLine + "```json\n" + json + "\n```";
            return Finish(body);
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

        string Finish(string payload) =>
            ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(payload));
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
            var write = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newDocument.Project.Solution, cancellationToken).ConfigureAwait(false);
            if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceAndReturn(toolName, write.FormatAdapterMessage($"Added test stub `{methodName}` to `{className}`."));
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Added test stub `{methodName}` to `{className}`. Files touched: {write.SavedPaths.Count}.");
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
        bool? noBuild,
        string? buildPolicy,
        bool omittedNoBuildSkipsRebuild,
        bool noRestore,
        string? configuration,
        string? platform,
        string? binariesPath,
        bool includeFullOutput,
        int maxOutputChars,
        string? reportCursor,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(reportCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    DiagnosticReportAttachment.FormatChunkResponse(
                        DiagnosticReportStore.TryTakeChunk(reportCursor)));
            }

            var route = TestBuildRoute.Resolve(
                dllRoute: !string.IsNullOrWhiteSpace(binariesPath),
                buildPolicy,
                noBuild,
                omittedNoBuildSkipsRebuild);
            if (route.Error is not null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, route.Error);
            }

            if (maxOutputChars < 0)
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `maxOutputChars` must be >= 0.");
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` is empty.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(workspacePath);
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

            string? effectiveConfiguration;
            try
            {
                effectiveConfiguration = DotNetConfigurationArguments.Coalesce(
                    configuration, _solutionManager.LoadedConfiguration, nameof(configuration));
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            if (route.DllRoute)
            {
                return await ExecuteDllTestsAsync(
                        toolName,
                        targetPath,
                        workDir,
                        filter,
                        filterDescription,
                        requireFilterMatch,
                        timeoutSeconds,
                        noRestore,
                        effectiveConfiguration,
                        platform,
                        binariesPath!,
                        route.Policy,
                        includeFullOutput,
                        maxOutputChars,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            string? effectivePlatform;
            try
            {
                effectivePlatform = DotNetConfigurationArguments.CoalescePlatformForTarget(
                    platform,
                    _solutionManager.LoadedPlatformRaw,
                    _solutionManager.LoadedPlatform,
                    targetPath);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            DotNetTestArguments.CliPlan plan;
            try
            {
                plan = DotNetTestArguments.BuildPlan(
                    targetPath,
                    filter,
                    route.NoBuild,
                    noRestore,
                    effectiveConfiguration,
                    effectivePlatform,
                    _solutionManager.LoadedBuildArgs);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            var preTestBuildMeta = plan.IncludesPreTestBuild
                ? "yes (`dotnet build` then `dotnet test --no-build`)"
                : "skipped (`noBuild=true`)";
            var extraMeta =
                $"- **Configuration:** {(string.IsNullOrWhiteSpace(effectiveConfiguration) ? "(SDK/solution default)" : effectiveConfiguration)}"
                + Environment.NewLine
                + $"- **Platform:** {(string.IsNullOrWhiteSpace(effectivePlatform) ? "(SDK/solution default)" : effectivePlatform)}"
                + Environment.NewLine
                + $"- **BuildArgs:** {DotNetBuildArguments.FormatMetadata(_solutionManager.LoadedBuildArgs)}"
                + Environment.NewLine
                + $"- **PreTestBuild:** {preTestBuildMeta}";

            var outputOptions = new TestOutputReportOptions(includeFullOutput, maxOutputChars);
            extraMeta += Environment.NewLine + outputOptions.FormatMetadata();

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cliProgress = McpToolProgressReporter.TryCreate(progress);
            int? previousExit = null;

            var preTestBuildArguments = plan.PreTestBuildArguments;
            if (preTestBuildArguments is not null)
            {
                var buildRun = await RunCliAsync(
                    preTestBuildArguments,
                    workDir,
                    timeout,
                    previousExit: null,
                    CliProgressStep.BuildStage,
                    cliProgress,
                    cancellationToken).ConfigureAwait(false);

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
                    var timedOutText = timedOut.ToString().TrimEnd();
                    timedOutText = DiagnosticReportAttachment.AttachToResponse(
                        timedOutText,
                        buildRun.CombinedOutput,
                        DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(timedOutText));
                    return ToolTelemetry.TraceAndReturn(toolName, timedOutText);
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
                    var failedText = failed.ToString().TrimEnd();
                    failedText = DiagnosticReportAttachment.AttachToResponse(
                        failedText,
                        buildRun.CombinedOutput,
                        DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(failedText)
                        || DiagnosticReportAttachment.IsUnparsedBuildFailure(failedText));
                    return ToolTelemetry.TraceAndReturn(toolName, failedText);
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

                previousExit = buildRun.ExitCode;
            }

            var run = await RunCliAsync(
                plan.TestArguments,
                workDir,
                timeout,
                previousExit,
                CliProgressStep.TestStage,
                cliProgress,
                cancellationToken).ConfigureAwait(false);

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
                var timedOutText = sb.ToString().TrimEnd();
                timedOutText = DiagnosticReportAttachment.AttachToResponse(
                    timedOutText,
                    run.CombinedOutput,
                    DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(timedOutText));
                return ToolTelemetry.TraceAndReturn(toolName, timedOutText);
            }

            var parse = VstestOutputParser.Parse(run.CombinedOutput, run.ExitCode);
            var markdown = VstestOutputParser.BuildMarkdownReport(
                parse,
                run.ExitCode,
                run.CombinedOutput,
                filter,
                filterDescription,
                requireFilterMatch,
                outputOptions);

            if (requireFilterMatch
                && markdown.Contains("## Filtered test run — no matching tests", StringComparison.Ordinal))
            {
                var agentHint = WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(
                    _solutionManager.GetLoadedWorkspacePath(),
                    filterDescription,
                    targetPath);
                markdown = markdown + Environment.NewLine + Environment.NewLine + agentHint;
            }

            string finalMarkdown;
            if (run.ExitCode != 0 && VstestOutputParser.IsSilentUnparsedFailure(parse, run.CombinedOutput))
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
                finalMarkdown = sb.ToString().TrimEnd();
            }
            else
            {
                finalMarkdown = markdown + Environment.NewLine + Environment.NewLine + run.RunMetadata
                    + Environment.NewLine + extraMeta;
            }

            var shouldStore = DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(finalMarkdown)
                              || DiagnosticReportAttachment.IsPartialOrUnparsedTestStatus(finalMarkdown);
            finalMarkdown = DiagnosticReportAttachment.AttachToResponse(
                finalMarkdown,
                run.CombinedOutput,
                shouldStore);
            return ToolTelemetry.TraceAndReturn(toolName, finalMarkdown);
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

    private async Task<string> ExecuteDllTestsAsync(
        string toolName,
        string targetCsprojPath,
        string testWorkingDirectory,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch,
        int timeoutSeconds,
        bool noRestore,
        string? configuration,
        string? platform,
        string binariesPath,
        TestBuildPolicy policy,
        bool includeFullOutput,
        int maxOutputChars,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        var resolvedBinariesPath = _solutionManager.ResolvePathAgainstWorkspace(binariesPath);
        var loadedWorkspacePath = _solutionManager.GetLoadedWorkspacePath();
        var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken)
            .ConfigureAwait(false);
        var projects = solution?.Projects ?? Enumerable.Empty<Project>();
        var matchCount = 0;
        ProjectId? projectId = null;
        var hints = new List<TestAssemblyPathResolver.ProjectHint>();
        foreach (var project in projects)
        {
            hints.Add(new TestAssemblyPathResolver.ProjectHint(project.FilePath, project.AssemblyName));
            if (!SameProjectPath(project.FilePath, targetCsprojPath))
            {
                continue;
            }

            matchCount++;
            projectId = project.Id;
        }

        if (matchCount > 1)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                "Error: `workspacePath` matches more than one project in the loaded workspace.");
        }

        var resolved = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath,
            targetCsprojPath,
            resolvedBinariesPath,
            hints,
            requireExists: false);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.AssemblyPath) || projectId is null)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                resolved.ErrorMessage ?? "Error: could not resolve `binariesPath`.");
        }

        string? effectivePlatform;
        try
        {
            effectivePlatform = DotNetConfigurationArguments.CoalescePlatformForTarget(
                platform,
                _solutionManager.LoadedPlatformRaw,
                _solutionManager.LoadedPlatform,
                loadedWorkspacePath);
        }
        catch (ArgumentException ex)
        {
            return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
        }

        var context = new BuildContext(
            configuration,
            effectivePlatform,
            _solutionManager.LoadedTargetFramework,
            _solutionManager.LoadedBuildArgs,
            resolved.AssemblyPath);
        var cliProgress = McpToolProgressReporter.TryCreate(progress);
        var ensure = new TestDllEnsure(_solutionManager.SessionBuildState, CliRunner(cliProgress), cliProgress);
        var outcome = await ensure.ExecuteAsync(
                new TestDllEnsureRequest
                {
                    ProjectId = projectId,
                    Context = context,
                    Policy = policy,
                    PullSnapshot = _solutionManager.PullInputSnapshot,
                    SolutionPath = loadedWorkspacePath ?? string.Empty,
                    ProjectNeedle = Path.GetFileNameWithoutExtension(targetCsprojPath),
                    AssemblyPath = resolved.AssemblyPath,
                    AssemblyExists = File.Exists,
                    NoRestore = noRestore,
                    Filter = filter,
                    TestWorkingDirectory = testWorkingDirectory,
                    BuildWorkingDirectory = string.IsNullOrWhiteSpace(loadedWorkspacePath)
                        ? testWorkingDirectory
                        : WorkspaceRootResolver.ResolveDotNetWorkingDirectory(loadedWorkspacePath),
                    Timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ToolTelemetry.TraceAndReturn(
            toolName,
            FormatDllOutcome(
                outcome,
                filter,
                filterDescription,
                requireFilterMatch,
                targetCsprojPath,
                configuration,
                effectivePlatform,
                resolvedBinariesPath,
                includeFullOutput,
                maxOutputChars));
    }

    private TestDllEnsure.Runner CliRunner(ICliProgressReporter? progress)
    {
        return (arguments, workingDirectory, timeout, previousExit, cancellationToken) =>
            RunCliAsync(
                arguments,
                workingDirectory,
                timeout,
                previousExit,
                arguments.StartsWith("test ", StringComparison.Ordinal)
                    ? CliProgressStep.TestStage
                    : CliProgressStep.BuildStage,
                progress,
                cancellationToken);
    }

    private Task<DotNetCliRunner.RunResult> RunCliAsync(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        int? previousExit,
        string stage,
        ICliProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        if (_cliRunner is not null)
        {
            return _cliRunner(arguments, workingDirectory, timeout, previousExit, cancellationToken);
        }

        return CliProgressStep.RunWithMetadataAsync(
            arguments,
            workingDirectory,
            cancellationToken,
            timeout,
            progress,
            stage,
            previousExit);
    }

    private string FormatDllOutcome(
        TestDllEnsureResult outcome,
        string? filter,
        string? filterDescription,
        bool requireFilterMatch,
        string targetPath,
        string? configuration,
        string? platform,
        string binariesPath,
        bool includeFullOutput,
        int maxOutputChars)
    {
        var extraMeta = FormatDllMetadata(
            outcome,
            configuration,
            platform,
            _solutionManager.LoadedBuildArgs,
            binariesPath,
            includeFullOutput,
            maxOutputChars);
        if (!outcome.TestsStarted)
        {
            return FormatDllStopped(outcome, extraMeta);
        }

        var run = outcome.TestRun;
        if (run is null)
        {
            return FormatDllStopped(outcome, extraMeta);
        }

        if (run.TimedOut)
        {
            var timedOut = new StringBuilder();
            timedOut.AppendLine("## Test run timed out");
            timedOut.AppendLine();
            timedOut.AppendLine(run.RunMetadata);
            timedOut.AppendLine(extraMeta);
            timedOut.AppendLine();
            timedOut.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
            timedOut.AppendLine();
            TruncatedProcessLog.AppendLastCharacters(timedOut, "Console output before kill:", run.CombinedOutput);
            var timedOutText = timedOut.ToString().TrimEnd();
            return DiagnosticReportAttachment.AttachToResponse(
                timedOutText,
                run.CombinedOutput,
                DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(timedOutText));
        }

        var outputOptions = new TestOutputReportOptions(includeFullOutput, maxOutputChars);
        var parse = VstestOutputParser.Parse(run.CombinedOutput, run.ExitCode);
        var markdown = VstestOutputParser.BuildMarkdownReport(
            parse,
            run.ExitCode,
            run.CombinedOutput,
            filter,
            filterDescription,
            requireFilterMatch,
            outputOptions);
        if (requireFilterMatch
            && markdown.Contains("## Filtered test run — no matching tests", StringComparison.Ordinal))
        {
            markdown = markdown
                + Environment.NewLine
                + Environment.NewLine
                + WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(
                    _solutionManager.GetLoadedWorkspacePath(),
                    filterDescription,
                    targetPath);
        }

        string finalMarkdown;
        if (run.ExitCode != 0 && VstestOutputParser.IsSilentUnparsedFailure(parse, run.CombinedOutput))
        {
            var silent = new StringBuilder();
            silent.AppendLine(markdown);
            silent.AppendLine();
            silent.AppendLine("### Silent / unparsed failure hints");
            silent.AppendLine(
                "Exit code ≠ 0 but no clear VSTest summary or MSBuild/NU diagnostics were parsed "
                + "(common after hung restore or locked `obj`).");
            silent.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: false));
            silent.AppendLine();
            silent.AppendLine(run.RunMetadata);
            silent.AppendLine(extraMeta);
            finalMarkdown = silent.ToString().TrimEnd();
        }
        else
        {
            finalMarkdown = markdown
                + Environment.NewLine
                + Environment.NewLine
                + run.RunMetadata
                + Environment.NewLine
                + extraMeta;
        }

        var shouldStore = DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(finalMarkdown)
                          || DiagnosticReportAttachment.IsPartialOrUnparsedTestStatus(finalMarkdown);
        return DiagnosticReportAttachment.AttachToResponse(finalMarkdown, run.CombinedOutput, shouldStore);
    }

    private static string FormatDllStopped(TestDllEnsureResult outcome, string extraMeta)
    {
        var body = new StringBuilder();
        if (outcome.Cancelled)
        {
            body.AppendLine(outcome.Error ?? "`dotnet test` was cancelled.");
            body.AppendLine();
            body.AppendLine(extraMeta);
            return body.ToString().TrimEnd();
        }

        if (outcome.BuildRun is { TimedOut: true })
        {
            body.AppendLine("## Pre-test build timed out");
            body.AppendLine();
            body.AppendLine(outcome.Error);
            body.AppendLine();
            body.AppendLine(outcome.BuildRun.RunMetadata);
            body.AppendLine(extraMeta);
            body.AppendLine();
            body.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
            body.AppendLine();
            TruncatedProcessLog.AppendLastCharacters(
                body,
                "Console output before kill:",
                outcome.BuildRun.CombinedOutput);
            var timedOutText = body.ToString().TrimEnd();
            return DiagnosticReportAttachment.AttachToResponse(
                timedOutText,
                outcome.BuildRun.CombinedOutput,
                DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(timedOutText));
        }

        if (outcome.BuildRun is not null && outcome.BuildRun.ExitCode != 0)
        {
            body.AppendLine("## Pre-test build failed");
            body.AppendLine();
            body.AppendLine(outcome.Error);
            body.AppendLine();
            body.AppendLine(outcome.BuildRun.RunMetadata);
            body.AppendLine(extraMeta);
            TruncatedProcessLog.AppendLastCharacters(
                body,
                TruncatedProcessLog.BuildPreambleBuildConsoleTail(outcome.BuildRun.ExitCode),
                outcome.BuildRun.CombinedOutput);
            var failedText = body.ToString().TrimEnd();
            return DiagnosticReportAttachment.AttachToResponse(
                failedText,
                outcome.BuildRun.CombinedOutput,
                DiagnosticReportAttachment.ClientResponseHasTruncatedExcerpt(failedText)
                || DiagnosticReportAttachment.IsUnparsedBuildFailure(failedText));
        }

        body.AppendLine(outcome.Error ?? "Error: tests were not started.");
        body.AppendLine();
        if (outcome.BuildRun is not null)
        {
            body.AppendLine(outcome.BuildRun.RunMetadata);
        }

        body.AppendLine(extraMeta);
        return body.ToString().TrimEnd();
    }

    private static string FormatDllMetadata(
        TestDllEnsureResult outcome,
        string? configuration,
        string? platform,
        string? buildArgs,
        string binariesPath,
        bool includeFullOutput,
        int maxOutputChars)
    {
        var preTest = outcome.BuildArguments is null
            ? "skipped (`" + outcome.Reason + "`)"
            : "yes (solution `-t` then `dotnet test --no-build`)";
        var text =
            $"- **Configuration:** {(string.IsNullOrWhiteSpace(configuration) ? "(SDK/solution default)" : configuration)}"
            + Environment.NewLine
            + $"- **Platform:** {(string.IsNullOrWhiteSpace(platform) ? "(SDK/solution default)" : platform)}"
            + Environment.NewLine
            + $"- **BuildArgs:** {DotNetBuildArguments.FormatMetadata(buildArgs)}"
            + Environment.NewLine
            + $"- **PreTestBuild:** {preTest}"
            + Environment.NewLine
            + $"- **Freshness:** {outcome.Reason}";
        if (!string.IsNullOrWhiteSpace(outcome.SolutionTarget))
        {
            text += Environment.NewLine + $"- **SolutionTarget:** `{outcome.SolutionTarget}` (`-t`)";
        }

        text += Environment.NewLine
            + $"- **BinariesPath:** {binariesPath}"
            + Environment.NewLine
            + $"- **TestAssembly:** {outcome.AssemblyPath}";
        text += Environment.NewLine + new TestOutputReportOptions(includeFullOutput, maxOutputChars).FormatMetadata();
        return text;
    }

    private static bool SameProjectPath(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
