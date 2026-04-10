using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

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

    private readonly ILogger<TestTools> _logger;

    public TestTools(ILogger<TestTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "run_dotnet_test", Title = "Run dotnet test")]
    [Description(
        "Runs `dotnet test` on the specified project or solution. Use this to verify behavior after writing tests or refactoring. Returns a clean summary of passed/failed tests.")]
    public async Task<string> RunDotNetTest(
        [Description("Path to .csproj, .sln, or test project directory (same parameter name as load_workspace / run_dotnet_build).")]
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunDotNetTest);

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

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{fullPath}\" --verbosity normal",
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

            var markdown = BuildMarkdownSummary(summary, failures, process.ExitCode, combined);
            return ToolTelemetry.TraceAndReturn(toolName, markdown);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`dotnet test` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunDotNetTest failed for {WorkspacePath}", workspacePath);
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

        // Prefer the last xUnit/VSTest-style one-line summary (most reliable totals).
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

        // VSTest multi-line block: "Total tests: N" then "Passed:" / "Failed:" (order may vary).
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
            // xUnit: lines between header and "Stack Trace:" without an explicit label.
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
        string combinedOutput)
    {
        var sb = new StringBuilder();

        if (summary is null)
        {
            sb.AppendLine("## Test run");
            sb.AppendLine();
            sb.AppendLine(
                $"Could not parse a standard test summary from the output (exit code `{exitCode}`).");
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

            return sb.ToString().TrimEnd();
        }

        var (total, passed, failed, skipped) = summary;

        if (failed == 0)
        {
            sb.AppendLine("## All tests passed successfully!");
            sb.AppendLine();
            sb.AppendLine(
                $"Total: **{total}** · Passed: **{passed}** · Failed: **{failed}**" +
                (skipped > 0 ? $" · Skipped: **{skipped}**" : string.Empty));
            sb.AppendLine();
            sb.AppendLine("Green run — great time to refactor or add coverage.");
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
