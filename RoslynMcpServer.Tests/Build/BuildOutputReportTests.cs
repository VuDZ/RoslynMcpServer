using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

[Collection("DiagnosticReportStore")]
public sealed class BuildOutputReportTests
{
    [Fact]
    public void Successful_build_is_silent_by_default()
    {
        var report = BuildOutputReport.Format(Warning, 0, "sdk-context", includeBuildWarnings: false);

        Assert.Equal("## Build succeeded", report);
    }

    [Fact]
    public void Opted_in_warnings_are_deduplicated_without_execution_context()
    {
        var report = BuildOutputReport.Format(Warning + "\n" + Warning, 0, "sdk-context", includeBuildWarnings: true);

        Assert.Contains("CS0168", report);
        Assert.Equal(1, report.Split("**warning**", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("sdk-context", report);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_keeps_errors_first_and_context(bool includeWarnings)
    {
        var warnings = string.Join('\n', Enumerable.Repeat(Warning, 1000));
        var output = warnings + "\nApp.cs(4,2): error CS1001: Identifier expected";
        var report = BuildOutputReport.Format(output, 1, "sdk-context", includeWarnings);

        Assert.Contains("CS1001", report);
        Assert.Contains("### Execution context", report);
        Assert.Contains("sdk-context", report);
        Assert.Equal(includeWarnings, report.Contains("CS0168", StringComparison.Ordinal));
        Assert.True(report.IndexOf("CS1001", StringComparison.Ordinal) < report.IndexOf("### Execution context", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostics_without_codes_are_still_reported()
    {
        var report = BuildOutputReport.Format(
            "App.csproj : warning : Custom warning", 0, "sdk-context", includeBuildWarnings: true);

        Assert.Contains("Custom warning", report);
        Assert.Contains("**warning**", report);
        Assert.DoesNotContain("sdk-context", report);
    }

    [Fact]
    public void Parsed_failure_does_not_repeat_the_build_log_for_a_summary_footer()
    {
        var report = BuildOutputReport.Format(
            "ordinary build log\nApp.cs(4,2): error CS1001: Identifier expected\n1 Error(s)",
            1, "sdk-context", includeBuildWarnings: false);

        Assert.Contains("CS1001", report);
        Assert.DoesNotContain("ordinary build log", report);
    }

    [Fact]
    public void Unparsed_error_is_kept_alongside_parsed_errors()
    {
        var report = BuildOutputReport.Format(
            "error CS1001: Identifier expected\nfatal error: custom task evidence\n" + Warning,
            1, "sdk-context", includeBuildWarnings: false);

        Assert.Contains("CS1001", report);
        Assert.Contains("custom task evidence", report);
        Assert.DoesNotContain("CS0168", report);
    }

    [Theory]
    [InlineData("error CS0168: Warning promoted to error")]
    [InlineData("error CS0168: warning CS0168: Warning promoted to error")]
    [InlineData("App.csproj : error NETSDK1005: Assets file is incomplete")]
    [InlineData("error NU1903: Warning promoted to error")]
    public void Warning_as_error_and_sdk_errors_are_always_visible(string diagnostic)
    {
        var report = BuildOutputReport.Format(diagnostic, 1, "sdk-context", includeBuildWarnings: false);

        Assert.Contains("**error**", report);
        Assert.Contains(diagnostic.Split(':')[^1].Trim(), report);
    }

    [Fact]
    public void Unparsed_failure_filters_warning_noise_before_taking_tail_and_keeps_raw_report()
    {
        var output = "Custom task failed: useful evidence\n" + string.Join('\n', Enumerable.Repeat(Warning, 1000));
        var report = BuildOutputReport.Format(output, 1, "sdk-context", includeBuildWarnings: false);

        Assert.Contains("useful evidence", report);
        Assert.DoesNotContain("CS0168", report);
        var cursor = report.Split("reportCursor=`", StringSplitOptions.None)[1].Split('`')[0];
        var chunk = DiagnosticReportStore.TryTakeChunk(cursor);
        Assert.True(chunk.Ok);
        Assert.Contains("CS0168", chunk.Chunk);
    }

    [Theory]
    [InlineData(true, false, "timed out")]
    [InlineData(false, true, "probe budget exhausted")]
    public void Incomplete_build_is_never_success(bool timedOut, bool budgetExhausted, string status)
    {
        var report = BuildOutputReport.Format(
            Warning, 0, "sdk-context", includeBuildWarnings: false, timedOut: timedOut, budgetExhausted: budgetExhausted);

        Assert.Contains(status, report);
        Assert.Contains("### Execution context", report);
        Assert.DoesNotContain("CS0168", report);
    }

    [Theory]
    [InlineData("build App.csproj", true)]
    [InlineData("  BUILD\tApp.csproj", true)]
    [InlineData("test App.csproj", false)]
    [InlineData("build-server shutdown", false)]
    public void Raw_command_only_applies_build_policy_to_the_build_verb(string command, bool expected)
    {
        Assert.Equal(expected, BuildOutputReport.IsBuildCommand(command));
    }

    private const string Warning = "App.cs(3,2): warning CS0168: Variable is never used";
}
