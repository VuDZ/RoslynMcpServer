using System.Collections;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Utilities;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

internal static class VstestTrxTestFiles
{
    internal static string ResultsDirectory(string command) => ReadArgument(command, "--results-directory ")!;

    internal static void WritePassingRun(string command)
    {
        var directory = ResultsDirectory(command);
        var target = ReadArgument(command, "test ")!;
        var source = Path.ChangeExtension(target, ".dll");
        if (target != source)
        {
            WriteInventory(directory, [source], ReadArgument(command, "--filter "));
        }
        WriteReport(directory, source, passed: 1);
    }

    internal static void WriteInventory(string directory, string[] sources, string? filter, string? buildError = null)
    {
        MsBuildBootstrapper.Register();
        WriteBinlog(directory, sources, filter, buildError);
    }

    internal static string WriteReport(
        string directory, string source, int passed = 0, int failed = 0, int skipped = 0, string? noMatchFilter = null,
        string? aborted = null)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var id = Guid.NewGuid();
        var outcomes = Enumerable.Repeat("Passed", passed).Concat(Enumerable.Repeat("Failed", failed))
            .Concat(Enumerable.Repeat("NotExecuted", skipped)).ToArray();
        var rows = outcomes.Select((outcome, index) => new XElement(ns + "UnitTestResult",
            new XAttribute("testId", id), new XAttribute("testName", $"Sample.Case(row: {index})"),
            new XAttribute("outcome", outcome), outcome == "Failed"
                ? new XElement(ns + "Output", new XElement(ns + "ErrorInfo", new XElement(ns + "Message", "Assertion from TRX")))
                : null));
        var noMatch = noMatchFilter is null ? null
            : $"No test matches the given testcase filter `{noMatchFilter}` in {source}";
        var report = new XElement(ns + "TestRun", new XAttribute("id", id),
            new XElement(ns + "Results", rows),
            outcomes.Length == 0 ? null : new XElement(ns + "TestDefinitions", new XElement(ns + "UnitTest",
                new XAttribute("id", id), new XAttribute("storage", source.ToLowerInvariant()),
                new XElement(ns + "TestMethod", new XAttribute("className", "Sample"), new XAttribute("name", "Case"),
                    new XAttribute("codeBase", source)))),
            new XElement(ns + "ResultSummary", new XAttribute("outcome", failed > 0 || aborted is not null ? "Failed" : "Completed"),
                new XElement(ns + "Counters", new XAttribute("total", outcomes.Length), new XAttribute("passed", passed),
                    new XAttribute("failed", failed), new XAttribute("notExecuted", 0)),
                noMatch is null && aborted is null ? null : new XElement(ns + "RunInfos", new XElement(ns + "RunInfo",
                    new XAttribute("outcome", aborted is null ? "Warning" : "Error"), new XElement(ns + "Text", aborted ?? noMatch)))));
        var path = Path.Combine(directory, $"{id:N}.trx");
        report.Save(path);
        return path;
    }

    private static string? ReadArgument(string command, string prefix)
    {
        var match = Regex.Match(command, Regex.Escape(prefix) + "\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static void WriteBinlog(string directory, string[] sources, string? filter, string? buildError)
    {
        var path = Path.Combine(directory, VstestTrxReader.BinlogName);
        var events = new SyntheticEventSource();
        var logger = new BinaryLogger { Parameters = path + ";ProjectImports=None", Verbosity = LoggerVerbosity.Diagnostic };
        logger.Initialize(events);
        events.Raise(new BuildStartedEventArgs("Build started", ""));
        for (var index = 0; index < sources.Length; index++)
        {
            var context = new BuildEventContext(1, 1, index + 1, 1);
            var project = Path.ChangeExtension(sources[index], ".csproj");
            events.Raise(new TaskStartedEventArgs("Started VSTestTask", "", project, "VSTest.targets", "VSTestTask")
                { BuildEventContext = context });
            WriteInput("TestFileFullPath", sources[index]);
            if (filter is not null)
            {
                WriteInput("VSTestTestCaseFilter", filter);
            }
            events.Raise(new TaskFinishedEventArgs("Finished VSTestTask", "", project, "VSTest.targets", "VSTestTask", true)
                { BuildEventContext = context });

            void WriteInput(string name, string value) => events.Raise(new TaskParameterEventArgs(
                TaskParameterMessageKind.TaskInput, name, new ArrayList { new TaskItem(value) }, false, DateTime.UtcNow)
                { BuildEventContext = context });
        }
        if (buildError is not null)
        {
            events.Raise(new BuildErrorEventArgs("", "CI-001", "Test.csproj", 1, 1, 1, 1, buildError, "", "MSBuild"));
        }
        events.Raise(new BuildFinishedEventArgs("Build finished", "", buildError is null));
        logger.Shutdown();
    }

    private sealed class SyntheticEventSource : IEventSource
    {
        public event AnyEventHandler? AnyEventRaised;
#pragma warning disable CS0067 // BinaryLogger consumes AnyEventRaised.
        public event BuildStartedEventHandler? BuildStarted;
        public event BuildFinishedEventHandler? BuildFinished;
        public event ProjectStartedEventHandler? ProjectStarted;
        public event ProjectFinishedEventHandler? ProjectFinished;
        public event TargetStartedEventHandler? TargetStarted;
        public event TargetFinishedEventHandler? TargetFinished;
        public event TaskStartedEventHandler? TaskStarted;
        public event TaskFinishedEventHandler? TaskFinished;
        public event BuildMessageEventHandler? MessageRaised;
        public event BuildWarningEventHandler? WarningRaised;
        public event BuildErrorEventHandler? ErrorRaised;
        public event CustomBuildEventHandler? CustomEventRaised;
        public event BuildStatusEventHandler? StatusEventRaised;
#pragma warning restore CS0067

        internal void Raise(BuildEventArgs args) => AnyEventRaised?.Invoke(this, args);
    }
}
