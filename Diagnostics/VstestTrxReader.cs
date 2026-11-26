using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Build;
using static RoslynMcpServer.Diagnostics.VstestOutputParser;

namespace RoslynMcpServer.Diagnostics;

internal static class VstestTrxReader
{
    internal const string BinlogName = "launches.binlog";
    internal const int MaxFailedTestDetails = 5;
    private const int MaxReportCharacters = 64 * 1024 * 1024;

    internal static VstestTrxResult Read(
        string directory, string? assemblyPath, string? filter, DotNetCliRunner.RunResult run,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inventory = VstestRunInventory.Read(Path.Combine(directory, BinlogName), assemblyPath, filter, cancellationToken);
        var expected = inventory.Sources.CountBy(source => source, VstestRunInventory.SourceComparer)
            .ToDictionary(pair => pair.Key, pair => pair.Value, VstestRunInventory.SourceComparer);
        var runIds = new HashSet<Guid>();
        var failures = new List<FailedTestDetail>();
        var passedNames = new HashSet<string>(StringComparer.Ordinal);
        var totals = new TestSummary(0, 0, 0, 0);
        var hasTestFailure = false;
        var failuresTruncated = false;
        string? diagnostic = null;
        string? issue = null;

        foreach (var path in Directory.EnumerateFiles(directory, "*.trx"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunReport report;
            try
            {
                report = ReadFile(path, filter);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException
                                       or InvalidDataException or ArgumentException)
            {
                issue ??= $"VSTest report '{Path.GetFileName(path)}' was unreadable or incomplete.";
                continue;
            }

            hasTestFailure |= report.Failed;
            diagnostic ??= report.Diagnostic;
            if (!runIds.Add(report.Id))
            {
                issue ??= "VSTest produced duplicate TRX run IDs.";
                continue;
            }

            if (report.Source is not { } source || !expected.TryGetValue(source, out var remaining) || remaining == 0)
            {
                issue ??= $"VSTest report '{Path.GetFileName(path)}' could not be matched to an expected launch.";
            }
            else
            {
                expected[source] = remaining - 1;
            }

            issue ??= report.Issue;
            checked
            {
                totals = new TestSummary(totals.Total + report.Summary.Total, totals.Passed + report.Summary.Passed,
                    totals.Failed + report.Summary.Failed, totals.Skipped + report.Summary.Skipped);
            }
            failuresTruncated |= failures.Count + report.FailureCount > MaxFailedTestDetails;
            failures.AddRange(report.Failures.Take(MaxFailedTestDetails - failures.Count));
            foreach (var name in report.PassedNames)
            {
                if (passedNames.Count < 5)
                {
                    passedNames.Add(name);
                }
            }
        }

        if (expected.Values.Any(count => count > 0))
        {
            issue ??= "One or more expected VSTest launches did not produce a matching complete TRX report.";
        }
        issue ??= inventory.Issue;
        var buildError = inventory.BuildError;
        if (run.ExitCode != 0 && buildError is null && runIds.Count == 0)
        {
            buildError = run.CombinedOutput.Split('\n').FirstOrDefault(BuildOutputReport.IsErrorDiagnostic)?.Trim();
        }

        var complete = issue is null && runIds.Count > 0 && run.ExitCode == 0;
        var failed = hasTestFailure || buildError is not null || (run.ExitCode != 0 && runIds.Count == 0);
        var noMatches = complete && !failed && filter is not null && totals.Total == 0;
        var outcome = failed || noMatches ? TestProcessOutcome.Failed
            : complete && totals.Total > 0 ? TestProcessOutcome.Passed
            : TestProcessOutcome.Partial;
        // Failed tests already explain a non-zero exit code.
        issue = diagnostic ?? issue ?? (run.ExitCode != 0 && !hasTestFailure ? "The command did not complete successfully." : null);
        return new(outcome, runIds.Count == 0 ? null : totals, failures, passedNames.ToArray(),
            failuresTruncated, noMatches, issue, buildError);
    }

    private static RunReport ReadFile(string path, string? filter)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxReportCharacters,
        });
        var root = XDocument.Load(reader).Root;
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        if (root?.Name != ns + "TestRun" || !Guid.TryParse((string?)root.Attribute("id"), out var id) || id == Guid.Empty
            || root.Element(ns + "ResultSummary") is not { } summary || summary.Element(ns + "Counters") is not { } counters)
        {
            throw new InvalidDataException("Missing TRX run identity or summary.");
        }

        var result = ReadCounters(counters);
        var outcome = (string?)summary.Attribute("outcome");
        var definitions = root.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest").ToArray() ?? [];
        var sources = definitions.Select(definition => VstestRunInventory.NormalizeSource(
                (string?)definition.Element(ns + "TestMethod")?.Attribute("codeBase"))
                ?? VstestRunInventory.NormalizeSource((string?)definition.Attribute("storage")))
            .Distinct(VstestRunInventory.SourceComparer).ToArray();
        var notices = summary.Element(ns + "RunInfos")?.Elements(ns + "RunInfo").ToArray() ?? [];
        var source = result.Total == 0 && filter is not null
            ? ReadNoMatchSource(notices, ns, filter)
            : sources.Length == 1 ? sources[0] : null;

        var names = definitions.Where(definition => definition.Attribute("id") is not null)
            .ToLookup(definition => (string)definition.Attribute("id")!, StringComparer.OrdinalIgnoreCase);
        var rows = root.Element(ns + "Results")?.Descendants(ns + "UnitTestResult")
            .Where(row => !row.Elements(ns + "InnerResults").Any()).ToArray() ?? [];
        var failedRows = rows.Where(row => IsFailure((string?)row.Attribute("outcome"))).ToArray();
        var passedRows = rows.Where(row => (string?)row.Attribute("outcome") == "Passed").ToArray();
        var skipped = rows.Count(row => (string?)row.Attribute("outcome") is "NotExecuted" or "Skipped");
        var failed = IsFailure(outcome) || result.Failed > 0 || failedRows.Length > 0;
        var consistent = rows.Length == result.Total && passedRows.Length == result.Passed
            && failedRows.Length == result.Failed && skipped == result.Skipped;
        // VSTest marks a run with an ordinary failed test as Failed. Consistent rows and counters still make the
        // report complete; calling it incomplete made agents distrust correct totals. An abort is reported
        // through the run notices below.
        var completeOutcome = outcome is "Completed" or "Passed" || (outcome == "Failed" && failedRows.Length > 0);
        var issue = !completeOutcome || !consistent
            ? "A TRX report did not establish complete test results."
            : null;
        var diagnostic = notices.Where(notice => IsFailure((string?)notice.Attribute("outcome")))
            .Select(notice => (string?)notice.Element(ns + "Text"))
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message) &&
                (failed && failedRows.Length == 0 || message.StartsWith("The active test run was aborted.", StringComparison.Ordinal)));
        failed |= diagnostic is not null;
        diagnostic = diagnostic is null ? null : ProcessOutputRedactor.Redact(diagnostic);
        return new(id, result, source, failed, issue, diagnostic, failedRows.Length,
            failedRows.Take(MaxFailedTestDetails).Select(row => ReadFailure(row, GetName(row))).ToArray(),
            passedRows.Select(GetName).Distinct(StringComparer.Ordinal).Take(5).ToArray());

        string GetName(XElement row)
        {
            var testId = (string?)row.Attribute("testId");
            var method = testId is null ? null : names[testId].FirstOrDefault()?.Element(ns + "TestMethod");
            if ((string?)method?.Attribute("className") is { Length: > 0 } type
                && (string?)method.Attribute("name") is { Length: > 0 } name)
            {
                return $"{type}.{name}";
            }

            return (string?)row.Attribute("testName") ?? "(unnamed test)";
        }
    }

    private static TestSummary ReadCounters(XElement counters)
    {
        var total = Read("total");
        var passed = Read("passed");
        var failed = Read("failed");
        if ((long)passed + failed > total)
        {
            throw new InvalidDataException("Inconsistent TRX counters.");
        }

        // The stock logger can report a skipped row as NotExecuted while the notExecuted counter stays zero.
        return new(total, passed, failed, total - passed - failed);

        int Read(string name) => int.TryParse((string?)counters.Attribute(name), NumberStyles.None,
            CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException($"Invalid TRX counter '{name}'.");
    }

    private static string? ReadNoMatchSource(IEnumerable<XElement> notices, XNamespace ns, string filter)
    {
        // Match the exact requested filter first. Both filter and path can contain the VSTest delimiter.
        var prefix = $"No test matches the given testcase filter `{filter}` in ";
        var sources = notices.Where(notice => (string?)notice.Attribute("outcome") == "Warning")
            .Select(notice => (string?)notice.Element(ns + "Text"))
            .Where(message => message is not null && message.StartsWith(prefix, StringComparison.Ordinal))
            .Select(message => VstestRunInventory.NormalizeSource(message![prefix.Length..]))
            .Distinct(VstestRunInventory.SourceComparer).ToArray();
        return sources.Length == 1 ? sources[0] : null;
    }

    private static FailedTestDetail ReadFailure(XElement row, string name)
    {
        var ns = row.Name.Namespace;
        var output = row.Element(ns + "Output");
        var error = output?.Element(ns + "ErrorInfo");
        return new(name, Excerpt(error?.Element(ns + "Message"), 4_000), Excerpt(error?.Element(ns + "StackTrace"), 1_200),
            Excerpt(output?.Element(ns + "StdOut"), TestOutputReportOptions.ParseStreamCapChars),
            Excerpt(output?.Element(ns + "StdErr"), TestOutputReportOptions.ParseStreamCapChars));
    }

    private static string Excerpt(XElement? element, int limit) =>
        TruncatedProcessLog.BuildTruncatedExcerpt(ProcessOutputRedactor.Redact(element?.Value ?? string.Empty), limit);

    private static bool IsFailure(string? outcome) => outcome is
        "Failed" or "Error" or "Aborted" or "Timeout" or "Disconnected" or "NotRunnable" or "PassedButRunAborted";

    private sealed record RunReport(
        Guid Id, TestSummary Summary, string? Source, bool Failed, string? Issue, string? Diagnostic, int FailureCount,
        IReadOnlyList<FailedTestDetail> Failures, IReadOnlyList<string> PassedNames);
}
