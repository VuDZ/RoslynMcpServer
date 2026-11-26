using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class VstestTrxReaderTests : IDisposable
{
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("wrong assembly")]
    [InlineData("duplicate run id")]
    public void Passing_report_cannot_hide_an_unconfirmed_launch(string scenario)
    {
        var first = Source("net10.0");
        var second = Source("net11.0");
        VstestTrxTestFiles.WriteInventory(_directory.FullName, [first, second], null);
        var report = VstestTrxTestFiles.WriteReport(_directory.FullName, first, passed: 1);
        var anotherPath = Path.Combine(_directory.FullName, "other.trx");
        switch (scenario)
        {
            case "malformed":
                File.WriteAllText(anotherPath, "<TestRun>");
                break;
            case "wrong assembly":
                VstestTrxTestFiles.WriteReport(_directory.FullName, Source("net9.0"), passed: 1);
                break;
            case "duplicate run id":
                File.Copy(report, anotherPath);
                break;
        }

        var result = Read();

        Assert.Equal(TestProcessOutcome.Partial, result.Outcome);
        Assert.NotNull(result.Issue);
    }

    [Fact]
    public void Complete_reports_sum_equal_counts_for_separate_targets()
    {
        var first = Source("net10.0");
        var second = Source("net11.0");
        VstestTrxTestFiles.WriteInventory(_directory.FullName, [first, second], null);
        VstestTrxTestFiles.WriteReport(_directory.FullName, first, passed: 2, skipped: 1);
        VstestTrxTestFiles.WriteReport(_directory.FullName, second, passed: 2, skipped: 1);

        var result = Read();

        Assert.Equal(TestProcessOutcome.Passed, result.Outcome);
        Assert.Equal(new VstestOutputParser.TestSummary(6, 4, 0, 2), result.Summary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_filter_matches_preserve_the_command_outcome(bool buildError)
    {
        const string Filter = "FullyQualifiedName~Sample";
        var first = Source("net10.0");
        var second = Source("net11.0");
        VstestTrxTestFiles.WriteInventory(_directory.FullName, [first, second], Filter,
            buildError ? "Intentional post-test error." : null);
        VstestTrxTestFiles.WriteReport(_directory.FullName, first, passed: 1);
        VstestTrxTestFiles.WriteReport(_directory.FullName, second, noMatchFilter: Filter);

        var result = Read(filter: Filter, exitCode: buildError ? 1 : 0);

        Assert.Equal(buildError ? TestProcessOutcome.Failed : TestProcessOutcome.Passed, result.Outcome);
        Assert.Equal(new VstestOutputParser.TestSummary(1, 1, 0, 0), result.Summary);
        Assert.Equal(buildError ? "CI-001: Intentional post-test error." : null, result.BuildError);
    }

    [Fact]
    public void A_successful_report_cannot_override_an_aborted_host()
    {
        var first = Source("net10.0");
        var second = Source("net11.0");
        VstestTrxTestFiles.WriteInventory(_directory.FullName, [first, second], null);
        VstestTrxTestFiles.WriteReport(_directory.FullName, first, passed: 1);
        VstestTrxTestFiles.WriteReport(_directory.FullName, second, aborted: "The active test run was aborted.");

        var result = Read(exitCode: 1);

        Assert.Equal(TestProcessOutcome.Failed, result.Outcome);
        Assert.Contains("aborted", result.Issue, StringComparison.Ordinal);
        Assert.Equal(1, result.Summary!.Passed);
    }

    [Fact]
    public void Trx_failure_details_survive_clipped_console_output()
    {
        var source = Source("net11.0");
        VstestTrxTestFiles.WriteReport(_directory.FullName, source, passed: 1, failed: 2);

        var result = Read(assembly: source, exitCode: 1);

        Assert.Equal(TestProcessOutcome.Failed, result.Outcome);
        Assert.Equal(new VstestOutputParser.TestSummary(3, 1, 2, 0), result.Summary);
        Assert.All(result.Failures, failure =>
        {
            Assert.Equal("Sample.Case", failure.Name);
            Assert.Equal("Assertion from TRX", failure.Error);
        });
    }

    [Fact]
    public void Passing_trx_and_nonzero_exit_is_partial()
    {
        var source = Source("net11.0");
        VstestTrxTestFiles.WriteReport(_directory.FullName, source, passed: 1);

        var result = Read(assembly: source, exitCode: 1);

        Assert.Equal(TestProcessOutcome.Partial, result.Outcome);
        Assert.Equal(1, result.Summary!.Passed);
        Assert.NotNull(result.Issue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_filter_matches_require_attributed_reports_for_the_requested_filter(bool differentFilter)
    {
        const string Filter = "FullyQualifiedName~Sample";
        var source = Source("net11.0");
        VstestTrxTestFiles.WriteInventory(_directory.FullName, [source], Filter);
        VstestTrxTestFiles.WriteReport(_directory.FullName, source,
            noMatchFilter: differentFilter ? "FullyQualifiedName~Other" : Filter);

        var result = Read(filter: Filter);

        Assert.Equal(differentFilter ? TestProcessOutcome.Partial : TestProcessOutcome.Failed, result.Outcome);
        Assert.Equal(!differentFilter, result.NoFilterMatches);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private string Source(string framework) => Path.Combine(_directory.FullName, framework, "Sample.dll");

    private VstestTrxResult Read(string? assembly = null, string? filter = null, int exitCode = 0)
    {
        const string RetainedOutput = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1";
        var run = new DotNetCliRunner.RunResult(exitCode, RetainedOutput, "", 900_000, 0)
        {
            OutputTruncated = true,
        };
        return VstestTrxReader.Read(_directory.FullName, assembly, filter, run, CancellationToken.None);
    }

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
}
