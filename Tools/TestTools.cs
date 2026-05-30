using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class TestTools
{
    private const int MaxFailedTestDetails = 5;
    private const int MaxStackTraceLinesPerFailure = 15;

    private static readonly Regex RxXunitFailLine = new(
        pattern: @"^\[xUnit\.net[^\]]*\]\s+(?<name>.+?)\s+\[FAIL\]\s*$",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>VSTest-style: "  Failed Namespace.Test [12 ms]"</summary>
    private static readonly Regex RxVstestFailedLine = new(
        pattern: @"^\s*Failed\s+(?<name>.+?)(?:\s+\[[\d\.]+\s*ms\])?\s*$",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>NUnit-style: "Failed : TestName"</summary>
    private static readonly Regex RxNunitFailedLine = new(
        pattern: @"^\s*Failed\s*:\s*(?<name>.+)\s*$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>xUnit / VSTest one-line summary at end of run.</summary>
    private static readonly Regex RxEndSummaryLine = new(
        pattern: @"(?<kind>Passed|Failed)!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxTotalTests = new(
        pattern: @"Total tests:\s*(?<total>\d+)",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxPassedLine = new(
        pattern: @"^\s*Passed:\s*(?<n>\d+)\s*$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RxFailedLine = new(
        pattern: @"^\s*Failed:\s*(?<n>\d+)\s*$",
        options: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly SolutionManager _solutionManager;
    private readonly ILogger<TestTools> _logger;

    public TestTools(SolutionManager solutionManager, ILogger<TestTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_test", Title = "Run dotnet test")]
    [Description(
        "Runs `dotnet test` on the specified project or solution. Use this to verify behavior after writing tests or refactoring. Returns a clean summary of passed/failed tests.")]
    public Task<string> RunDotNetTest(
        [Description("Path to .csproj, .sln, or test project directory (same parameter name as load_workspace / run_dotnet_build).")]
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDotnetTestAsync(nameof(RunDotNetTest), workspacePath, filter: null, filterDescription: null, cancellationToken);
    }

    [McpServerTool(Name = "run_specific_test", Title = "Run a filtered dotnet test")]
    [Description(
        "Runs `dotnet test` filtered to a single test class and/or method. Builds the VSTest `--filter` expression internally — " +
        "do not use `execute_dotnet_command` or hand-written FullyQualifiedName filters. " +
        "When the Roslyn workspace is loaded, resolves the exact fully qualified test name for precise filtering. " +
        "Use for TDD and bug fixes instead of running the full suite.")]
    public async Task<string> RunSpecificTest(
        [Description("Path to .csproj, .sln, or test project directory (same as run_dotnet_test).")]
        string workspacePath,
        [Description("Test class name (simple or fully qualified), e.g. `UserServiceTests`.")]
        string? className = null,
        [Description("Test method name, e.g. `CreateUser_WhenValid_ReturnsOk`.")]
        string? methodName = null,
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

            return await ExecuteDotnetTestAsync(toolName, workspacePath, filter, description, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`run_specific_test` was cancelled.");
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
                return ToolTelemetry.TraceAndReturn(toolName, "No workspace loaded. Call `load_workspace` first.");
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
        string filePath,
        string className,
        string methodName,
        [Description("xunit (default), nunit, or mstest.")] string? testFramework = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GenerateTestMethodStub);
        try
        {
            var fullPath = Path.GetFullPath(filePath);
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
                ? Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory
                : fullPath;

            var filterArg = string.IsNullOrWhiteSpace(filter)
                ? string.Empty
                : $" --filter \"{filter.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{fullPath}\" --verbosity normal{filterArg}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir
            };

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Failed to start `dotnet` process. Ensure the .NET SDK is on PATH.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = (await stdoutTask).TrimEnd();
            var stderr = (await stderrTask).TrimEnd();
            var combined = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrEmpty(s)));

            var summary = TryParseTestSummary(combined);
            var failures = ParseFailedTestBlocks(combined, MaxFailedTestDetails);

            var markdown = BuildMarkdownSummary(summary, failures, process.ExitCode, combined, filter, filterDescription);
            return ToolTelemetry.TraceAndReturn(toolName, markdown);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`dotnet test` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ToolName} failed for {WorkspacePath}", toolName, workspacePath);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Failed to run `dotnet test`: {ex.Message}");
        }
    }

    private static TestSummary? TryParseTestSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match? lastEnd = null;
        foreach (Match m in RxEndSummaryLine.Matches(text))
        {
            lastEnd = m;
        }

        if (lastEnd is { Success: true })
        {
            var total = int.Parse(lastEnd.Groups["total"].Value, CultureInfo.InvariantCulture);
            var passed = int.Parse(lastEnd.Groups["passed"].Value, CultureInfo.InvariantCulture);
            var failed = int.Parse(lastEnd.Groups["failed"].Value, CultureInfo.InvariantCulture);
            var skipped = int.Parse(lastEnd.Groups["skipped"].Value, CultureInfo.InvariantCulture);
            return new TestSummary(total, passed, failed, skipped);
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var tm = RxTotalTests.Match(lines[i].Trim());
            if (!tm.Success)
            {
                continue;
            }

            var total = int.Parse(tm.Groups["total"].Value, CultureInfo.InvariantCulture);
            int? passed = null;
            int? failed = null;
            var skipped = 0;

            for (var j = i; j < Math.Min(i + 24, lines.Length); j++)
            {
                var line = lines[j].TrimEnd();
                var pm = RxPassedLine.Match(line);
                if (pm.Success)
                {
                    passed = int.Parse(pm.Groups["n"].Value, CultureInfo.InvariantCulture);
                }

                var fm = RxFailedLine.Match(line);
                if (fm.Success)
                {
                    failed = int.Parse(fm.Groups["n"].Value, CultureInfo.InvariantCulture);
                }
            }

            if (passed is not null && failed is not null)
            {
                return new TestSummary(total, passed.Value, failed.Value, skipped);
            }
        }

        return null;
    }

    private static IReadOnlyList<FailedTestDetail> ParseFailedTestBlocks(string text, int maxCount)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var blocks = new List<(int StartLine, string Name)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd();

            var xm = RxXunitFailLine.Match(trimmed);
            if (xm.Success)
            {
                blocks.Add((i, xm.Groups["name"].Value.Trim()));
                continue;
            }

            var nm = RxNunitFailedLine.Match(trimmed);
            if (nm.Success)
            {
                blocks.Add((i, nm.Groups["name"].Value.Trim()));
                continue;
            }

            var vm = RxVstestFailedLine.Match(trimmed);
            if (vm.Success)
            {
                blocks.Add((i, vm.Groups["name"].Value.Trim()));
            }
        }

        var result = new List<FailedTestDetail>();
        for (var b = 0; b < blocks.Count && result.Count < maxCount; b++)
        {
            var start = blocks[b].StartLine;
            var name = blocks[b].Name;
            var end = b + 1 < blocks.Count ? blocks[b + 1].StartLine : lines.Length;
            var blockText = string.Join(Environment.NewLine, lines[start..end]);
            ExtractErrorAndStack(blockText, out var error, out var stack);
            result.Add(new FailedTestDetail(name, error, stack));
        }

        return result;
    }

    private static void ExtractErrorAndStack(string block, out string error, out string stack)
    {
        error = string.Empty;
        stack = string.Empty;

        var emIdx = block.IndexOf("Error Message:", StringComparison.OrdinalIgnoreCase);
        var stIdx = block.IndexOf("Stack Trace:", StringComparison.OrdinalIgnoreCase);

        if (emIdx >= 0)
        {
            var bodyStart = emIdx + "Error Message:".Length;
            var bodyEnd = stIdx >= 0 ? stIdx : block.Length;
            error = NormalizeDetailBody(block.AsSpan(bodyStart, bodyEnd - bodyStart));
        }
        else if (stIdx > 0)
        {
            var firstNl = block.IndexOf('\n');
            if (firstNl >= 0 && firstNl + 1 < stIdx)
            {
                error = NormalizeDetailBody(block.AsSpan(firstNl + 1, stIdx - firstNl - 1));
            }
        }

        if (stIdx >= 0)
        {
            var after = block[(stIdx + "Stack Trace:".Length)..].TrimStart();
            stack = TrimStackTrace(after);
        }
    }

    private static string NormalizeDetailBody(ReadOnlySpan<char> span)
    {
        var s = span.ToString().Trim();
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(t);
        }

        return sb.Length > 512 ? sb.ToString(0, 509) + "..." : sb.ToString();
    }

    private static string TrimStackTrace(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return string.Empty;
        }

        var stackLines = stack
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .Take(MaxStackTraceLinesPerFailure)
            .ToArray();

        var joined = string.Join(Environment.NewLine, stackLines);
        return joined.Length > 1200 ? joined[..1197] + "..." : joined;
    }

    private static string BuildMarkdownSummary(
        TestSummary? summary,
        IReadOnlyList<FailedTestDetail> failures,
        int exitCode,
        string combinedOutput,
        string? filter,
        string? filterDescription)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            sb.AppendLine("## Filtered test run");
            sb.AppendLine();
            sb.AppendLine($"**Filter:** `{EscapeMdBackticks(filter)}`");
            if (!string.IsNullOrWhiteSpace(filterDescription))
            {
                sb.AppendLine($"**Match:** {filterDescription}");
            }

            sb.AppendLine();
        }

        if (summary is null)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(filter) ? "## Test run" : "## Filtered test run");
            sb.AppendLine();
            sb.AppendLine(
                $"No standard VSTest/xUnit summary line was detected in the output (exit code `{exitCode}`).");
            if (failures.Count > 0)
            {
                sb.AppendLine();
                AppendFailureDetails(sb, failures, CountFailureAnchors(combinedOutput) > MaxFailedTestDetails);
            }
            else if (exitCode == 0)
            {
                sb.AppendLine();
                sb.AppendLine("Process exited successfully; treat results with caution if tests were expected.");
            }

            if (exitCode != 0)
            {
                TruncatedProcessLog.AppendLastCharacters(
                    sb,
                    TruncatedProcessLog.BuildPreambleTestFailed(exitCode),
                    combinedOutput);
            }

            return sb.ToString().TrimEnd();
        }

        var (total, passed, failed, skipped) = summary;

        if (failed == 0)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(filter) ? "## All tests passed successfully!" : "## Filtered tests passed");
            sb.AppendLine();
            sb.AppendLine(
                $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
                (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(filter)
                ? "Green run — great time to refactor or add coverage."
                : "Filtered run succeeded.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"❌ {failed} Tests Failed.");
        sb.AppendLine();
        sb.AppendLine(
            $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
            (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
        sb.AppendLine();

        var truncated = CountFailureAnchors(combinedOutput) > MaxFailedTestDetails;
        AppendFailureDetails(sb, failures, truncated);

        if (failures.Count == 0)
        {
            sb.AppendLine(
                "_Failure details could not be parsed from the log (format may differ). Inspect the test project locally or adjust verbosity._");
        }

        return sb.ToString().TrimEnd();
    }

    private static int CountFailureAnchors(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var n = 0;
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (RxXunitFailLine.IsMatch(trimmed) || RxNunitFailedLine.IsMatch(trimmed) || RxVstestFailedLine.IsMatch(trimmed))
            {
                n++;
            }
        }

        return n;
    }

    private static void AppendFailureDetails(StringBuilder sb, IReadOnlyList<FailedTestDetail> failures, bool truncated)
    {
        if (failures.Count == 0)
        {
            return;
        }

        for (var i = 0; i < failures.Count; i++)
        {
            var f = failures[i];
            sb.AppendLine($"{i + 1}. **TestName:** `{EscapeMdBackticks(f.Name)}`");
            if (!string.IsNullOrEmpty(f.Error))
            {
                sb.AppendLine($"   **Error:** {EscapeMdBackticks(f.Error)}");
            }

            if (!string.IsNullOrEmpty(f.Stack))
            {
                var stackOneLine = f.Stack.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
                sb.AppendLine($"   **Stack:** {EscapeMdBackticks(stackOneLine)}");
            }

            sb.AppendLine();
        }

        if (truncated)
        {
            sb.AppendLine("[!] Showing first 5 failures only.");
        }
    }

    private static string EscapeMdBackticks(string s)
    {
        return s.Replace('`', '\'');
    }

    private sealed record TestSummary(int Total, int Passed, int Failed, int Skipped);

    private sealed record FailedTestDetail(string Name, string Error, string Stack);
}
