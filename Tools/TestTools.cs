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
        "Runs `dotnet test` on the specified project or solution. Use this to verify behavior after writing tests or refactoring. " +
        "Returns a clean summary of passed/failed tests. Default timeout 300s; on timeout/cancel the process tree is killed. " +
        "After `run_dotnet_build`, pass `noBuild=true` (and optionally `noRestore=true`) to skip rebuild. " +
        "For long integration tests raise `timeoutSeconds` (e.g. 900/1800).")]
    public Task<string> RunDotNetTest(
        [Description("Path to .csproj, .sln, or test project directory (directories allowed; unlike `run_dotnet_build` which requires a file).")]
        string workspacePath,
        [Description("Process timeout in seconds. Default 300. Set 0 to disable timeout (not recommended).")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description("Pass `--no-build` (skip rebuild; use after a successful `run_dotnet_build`).")]
        bool noBuild = false,
        [Description("Pass `--no-restore` (skip NuGet restore).")]
        bool noRestore = false,
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
            cancellationToken);
    }

    [McpServerTool(Name = "run_specific_test", Title = "Run a filtered dotnet test")]
    [Description(
        "Runs `dotnet test` filtered to a single test class and/or method. Builds the VSTest `--filter` expression internally — " +
        "do not use `execute_dotnet_command` or hand-written FullyQualifiedName filters. " +
        "When the Roslyn workspace is loaded, resolves the exact fully qualified test name for precise filtering. " +
        "Use for TDD and bug fixes instead of running the full suite. Default timeout 300s; kills process tree on timeout/cancel. " +
        "After `run_dotnet_build`, pass `noBuild=true` (and optionally `noRestore=true`). Raise `timeoutSeconds` for slow tests.")]
    public async Task<string> RunSpecificTest(
        [Description("Path to .csproj, .sln, or test project directory (same as run_dotnet_test; directories allowed).")]
        string workspacePath,
        [Description("Test class name (simple or fully qualified), e.g. `UserServiceTests`.")]
        string? className = null,
        [Description("Test method name, e.g. `CreateUser_WhenValid_ReturnsOk`.")]
        string? methodName = null,
        [Description("Process timeout in seconds. Default 300. Set 0 to disable timeout (not recommended).")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        [Description("Pass `--no-build` (skip rebuild; use after a successful `run_dotnet_build`).")]
        bool noBuild = false,
        [Description("Pass `--no-restore` (skip NuGet restore).")]
        bool noRestore = false,
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

            var solution = _solutionManager.GetCurrentSolution();
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
    [Description("Returns JSON list of test methods (Fact/Theory/TestMethod/etc.) from the loaded solution.")]
    public async Task<string> GetTestList(
        [Description("Maximum tests to return (default 200).")] int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetTestList);
        try
        {
            var solution = _solutionManager.GetCurrentSolution();
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("No workspace loaded."));
            }

            var json = await TestDiscoveryHelper.ListTestsJsonAsync(solution, maxResults, cancellationToken)
                .ConfigureAwait(false);
            return ToolTelemetry.TraceAndReturn(toolName, "```json\n" + json + "\n```");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTestList failed");
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "generate_test_method_stub", Title = "Generate test method stub")]
    [Description("Inserts a test method stub ([Fact]/[Test]/[TestMethod]) into a test class via Roslyn AST.")]
    public async Task<string> GenerateTestMethodStub(
        [Description("Absolute or workspace-relative path to the test .cs file.")] string filePath,
        [Description("Test class name that will receive the stub.")] string className,
        [Description("New test method name to insert.")] string methodName,
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
                    && !string.Equals(ext, ".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"When passing a file, it must be a `.csproj` or `.sln`: `{fullPath}`");
                }
            }

            var workDir = File.Exists(fullPath)
                ? WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath)
                : WorkspaceRootResolver.FindDirectoryContainingGlobalJson(fullPath) ?? fullPath;

            var targetPath = File.Exists(fullPath)
                ? fullPath
                : WorkspaceRootResolver.FindSolutionOrProjectInDirectory(fullPath) ?? fullPath;

            var testArgs = DotNetTestArguments.Build(targetPath, filter, noBuild, noRestore);

            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var run = await DotNetCliRunner.RunWithMetadataAsync(
                testArgs,
                workDir,
                cancellationToken,
                timeout).ConfigureAwait(false);

            if (run.TimedOut)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Test run timed out");
                sb.AppendLine();
                sb.AppendLine(run.RunMetadata);
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
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                markdown + Environment.NewLine + Environment.NewLine + run.RunMetadata);
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
