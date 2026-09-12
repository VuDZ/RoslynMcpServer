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
        + "Prefer run_specific_test for one class or method. Omit configuration/platform to inherit load_workspace. Pre-test `dotnet build` also inherits load_workspace `buildArgs`.")]
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
        [Description("Bin directory with AssemblyName.dll. Needs loaded .sln/.slnx and a .csproj workspacePath. noBuild=false builds via solution `-t`.")]
        string? binariesPath = null,
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
            binariesPath,
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
        [Description("Bin directory with AssemblyName.dll. Needs loaded .sln/.slnx and a .csproj workspacePath. noBuild=false builds via solution `-t`.")]
        string? binariesPath = null,
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

            var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
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
                    binariesPath,
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

    [McpServerTool(Name = "run_test_by_filter", Title = "Run tests by VSTest filter")]
    [Description(
        "Runs dotnet test with a raw VSTest --filter. Executes a process. "
        + "Prefer run_specific_test for one class or method. Omit configuration/platform to inherit load_workspace.")]
    public Task<string> RunTestByFilter(
        [Description("Path to a .csproj, .sln, .slnx, or test project directory.")]
        string workspacePath,
        [Description("VSTest --filter string, passed through (e.g. FullyQualifiedName~MyClass).")]
        string filter,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description("Skip rebuild. Default true. Use after a successful run_dotnet_build.")]
        bool noBuild = true,
        [Description("Skip NuGet restore.")]
        bool noRestore = false,
        [Description("MSBuild Configuration. Omit to inherit load_workspace.")]
        string? configuration = null,
        [Description("MSBuild Platform. Omit to inherit load_workspace.")]
        string? platform = null,
        [Description("Bin directory with AssemblyName.dll. Needs loaded .sln/.slnx and a .csproj workspacePath. noBuild=false builds via solution `-t`.")]
        string? binariesPath = null,
        CancellationToken cancellationToken = default)
    {
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
            noRestore,
            configuration,
            platform,
            binariesPath,
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
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    _solutionManager.FormatNoPublishedSolutionMessage("No workspace loaded."));
            }

            var listed = await TestDiscoveryHelper.ListTestsJsonAsync(
                    solution, maxResults, projectName, nameContains, cancellationToken)
                .ConfigureAwait(false);
            if (!listed.Success)
            {
                return ToolTelemetry.TraceAndReturn(toolName, listed.Payload);
            }

            var json = listed.Payload;
            var loadedPath = _solutionManager.GetLoadedWorkspacePath();
            if (IsEmptyTestListPayload(json))
            {
                var guidance = listed.FiltersApplied
                    ? WorkspaceLoadGuidance.FormatFilteredTestListEmptyMessage(
                        loadedPath, projectName, nameContains)
                    : WorkspaceLoadGuidance.FormatEmptyTestListMessage(
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
        bool noBuild,
        bool noRestore,
        string? configuration,
        string? platform,
        string? binariesPath,
        CancellationToken cancellationToken)
    {
        try
        {
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
            string? effectivePlatform;
            try
            {
                effectiveConfiguration = DotNetConfigurationArguments.Coalesce(
                    configuration, _solutionManager.LoadedConfiguration, nameof(configuration));
                effectivePlatform = DotNetConfigurationArguments.CoalescePlatform(
                    platform, _solutionManager.LoadedPlatform);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            string? testAssemblyPath = null;
            string? resolvedBinariesPath = null;
            string? solutionTarget = null;
            string? slnPreTestBuildArguments = null;
            string? slnBuildWorkDir = null;
            if (!string.IsNullOrWhiteSpace(binariesPath))
            {
                resolvedBinariesPath = _solutionManager.ResolvePathAgainstWorkspace(binariesPath);
                var loadedWorkspacePath = _solutionManager.GetLoadedWorkspacePath();
                var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken)
                    .ConfigureAwait(false);
                var projects = (solution?.Projects ?? Enumerable.Empty<Microsoft.CodeAnalysis.Project>())
                    .Select(p => new TestAssemblyPathResolver.ProjectHint(p.FilePath, p.AssemblyName));
                var resolved = TestAssemblyPathResolver.TryResolve(
                    loadedWorkspacePath,
                    targetPath,
                    resolvedBinariesPath,
                    projects,
                    requireExists: noBuild);
                if (!resolved.Success)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        resolved.ErrorMessage ?? "Error: could not resolve `binariesPath`.");
                }

                testAssemblyPath = resolved.AssemblyPath;

                if (!noBuild)
                {
                    if (string.IsNullOrWhiteSpace(loadedWorkspacePath))
                    {
                        return ToolTelemetry.TraceAndReturn(
                            toolName,
                            "Error: `binariesPath` requires a `.sln` or `.slnx` workspace loaded. Call `load_workspace` first.");
                    }

                    var projectNeedle = Path.GetFileNameWithoutExtension(targetPath);
                    var targetResolved = SolutionProjectTargetResolver.TryResolve(
                        loadedWorkspacePath, projectNeedle);
                    if (!targetResolved.Success)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            toolName,
                            targetResolved.ErrorMessage ?? "Error: could not resolve the solution build target.");
                    }

                    solutionTarget = targetResolved.TargetName;
                    try
                    {
                        slnPreTestBuildArguments = DotNetTestArguments.BuildPreTestBuild(
                            loadedWorkspacePath,
                            noRestore,
                            effectiveConfiguration,
                            effectivePlatform,
                            _solutionManager.LoadedBuildArgs,
                            solutionTarget);
                    }
                    catch (ArgumentException ex)
                    {
                        return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
                    }

                    slnBuildWorkDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(loadedWorkspacePath);
                }
            }

            DotNetTestArguments.CliPlan plan;
            try
            {
                plan = DotNetTestArguments.BuildPlan(
                    targetPath,
                    filter,
                    noBuild,
                    noRestore,
                    effectiveConfiguration,
                    effectivePlatform,
                    _solutionManager.LoadedBuildArgs,
                    testAssemblyPath);
            }
            catch (ArgumentException ex)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
            }

            var preTestBuildMeta = slnPreTestBuildArguments is not null
                ? "yes (`dotnet build` solution `-t` then `dotnet test --no-build`)"
                : plan.IncludesPreTestBuild
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
            if (!string.IsNullOrWhiteSpace(solutionTarget))
            {
                extraMeta +=
                    Environment.NewLine
                    + $"- **SolutionTarget:** `{solutionTarget}` (`-t`)";
            }

            if (!string.IsNullOrWhiteSpace(resolvedBinariesPath))
            {
                extraMeta +=
                    Environment.NewLine
                    + $"- **BinariesPath:** {resolvedBinariesPath}"
                    + Environment.NewLine
                    + $"- **TestAssembly:** {testAssemblyPath}";
            }

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var preTestBuildArguments = slnPreTestBuildArguments ?? plan.PreTestBuildArguments;
            var preTestWorkDir = slnBuildWorkDir ?? workDir;
            if (preTestBuildArguments is not null)
            {
                var buildRun = await DotNetCliRunner.RunWithMetadataAsync(
                    preTestBuildArguments,
                    preTestWorkDir,
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

                if (slnPreTestBuildArguments is not null)
                {
                    var ensured = TestAssemblyPathResolver.EnsureAssemblyExists(
                        testAssemblyPath, afterSolutionTargetBuild: true);
                    if (!ensured.Success)
                    {
                        var missing = new StringBuilder();
                        missing.AppendLine(ensured.ErrorMessage ?? "Error: test assembly not found.");
                        missing.AppendLine();
                        missing.AppendLine(buildRun.RunMetadata);
                        missing.AppendLine(extraMeta);
                        return ToolTelemetry.TraceAndReturn(toolName, missing.ToString().TrimEnd());
                    }
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
