using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Diagnostics;

internal sealed class VstestRunInventory
{
    internal static StringComparer SourceComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal List<string> Sources { get; } = [];

    internal string? Issue { get; private set; }

    internal string? BuildError { get; private set; }

    /// <summary>MSBuild reported a failed VSTest task or build after every launch was identified.</summary>
    internal bool LaunchFailed { get; private set; }

    internal static VstestRunInventory Read(
        string binlogPath, string? assemblyPath, string? requestedFilter, CancellationToken cancellationToken)
    {
        var inventory = new VstestRunInventory();
        if (assemblyPath is not null)
        {
            inventory.Sources.Add(Path.GetFullPath(assemblyPath));
            return inventory;
        }

        try
        {
            inventory.ReadBinlog(binlogPath, requestedFilter, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or InvalidOperationException or NotSupportedException)
        {
            inventory.Issue = "The VSTest launch inventory could not be read from the MSBuild binary log.";
        }

        return inventory;
    }

    internal static string? NormalizeSource(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;

    private void ReadBinlog(string binlogPath, string? requestedFilter, CancellationToken cancellationToken)
    {
        // This records the tasks MSBuild actually selected, including conditional TFMs and solution configurations.
        // Counting TRX files alone cannot detect a test task that started but never wrote a report.
        var tasks = new Dictionary<BuildEventContext, TestTask>();
        bool? buildSucceeded = null;
        var replay = new BinaryLogReplayEventSource { AllowForwardCompatibility = true };
        replay.RecoverableReadError += _ => Issue = "The MSBuild binary log contained unreadable records.";
        replay.BuildFinished += (_, args) => buildSucceeded = args.Succeeded;
        replay.TaskStarted += (_, args) =>
        {
            if (args.BuildEventContext is { } context && args.TaskName is ("VSTestTask" or "VSTestTask2")
                && !tasks.TryAdd(context, new TestTask()))
            {
                Issue = "The MSBuild binary log repeated a VSTest launch identity.";
            }
        };
        replay.TaskFinished += (_, args) =>
        {
            if (args.BuildEventContext is { } context && tasks.TryGetValue(context, out var task))
            {
                task.Succeeded = args.Succeeded;
            }
        };
        replay.ErrorRaised += (_, args) =>
        {
            if (BuildError is null && (args.BuildEventContext is not { } context || !tasks.ContainsKey(context)))
            {
                BuildError = ProcessOutputRedactor.Redact($"{args.Code}: {args.Message}");
            }
        };
        replay.AnyEventRaised += (_, args) =>
        {
            if (args is TaskParameterEventArgs { Kind: TaskParameterMessageKind.TaskInput, BuildEventContext: { } context } input
                && tasks.TryGetValue(context, out var task)
                && input.Items is { Count: 1 } && input.Items[0] is ITaskItem item)
            {
                if (input.ItemType == "TestFileFullPath")
                {
                    task.Source = NormalizeSource(item.ItemSpec);
                }
                else if (input.ItemType == "VSTestTestCaseFilter")
                {
                    task.Filter = item.ItemSpec;
                }
            }
        };

        using var stream = File.OpenRead(binlogPath);
        replay.Replay(stream, cancellationToken);
        if (buildSucceeded is null || tasks.Count == 0 || tasks.Values.Any(task => task.Source is null || task.Succeeded is null))
        {
            Issue ??= "The MSBuild binary log did not identify every VSTest launch and its completion.";
        }
        else if (requestedFilter is not null && tasks.Values.Any(task => task.Filter != requestedFilter))
        {
            Issue ??= "One or more VSTest launches did not use the requested filter.";
        }
        else if (buildSucceeded != true || tasks.Values.Any(task => task.Succeeded != true))
        {
            // VSTestTask also fails for an ordinary failed test. The TRX reader decides whether failed rows explain it.
            LaunchFailed = true;
        }

        Sources.AddRange(tasks.Values.Select(task => task.Source).OfType<string>());
    }

    private sealed class TestTask
    {
        internal string? Source { get; set; }

        internal string? Filter { get; set; }

        internal bool? Succeeded { get; set; }
    }
}
