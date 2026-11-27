using System.Text.Json;
using Microsoft.Build.Framework;
using RoslynMcpServer.Services.Analyzers;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Analyzers;

public sealed class DesignTimeBuildTimingsTests
{
    public DesignTimeBuildTimingsTests()
    {
        MsBuildBootstrapper.Register();
    }

    [Fact]
    public void Nested_builds_are_inclusive_and_contexts_from_different_files_do_not_collide()
    {
        var timings = new DesignTimeBuildTimings();
        timings.Observe(0, Started(1, "Root.csproj", 0));
        timings.Observe(0, Started(2, "Child.csproj", 10));
        timings.Observe(1, Started(1, "Root.csproj", 20));
        timings.Observe(1, Finished(1, "Root.csproj", 60, false));
        timings.Observe(0, Finished(2, "Child.csproj", 70, true));
        timings.Observe(0, Finished(1, "Root.csproj", 100, true));

        using var document = JsonDocument.Parse(timings.ToJson(150, []));
        var root = document.RootElement;
        Assert.Equal("complete", root.GetProperty("status").GetString());
        Assert.Equal(100, root.GetProperty("binlogSpanMs").GetDouble());
        Assert.Equal(50, root.GetProperty("unattributedOpenMs").GetDouble());
        var projects = root.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal(2, projects.Length);
        Assert.Equal("Root.csproj", projects[0].GetProperty("project").GetString());
        Assert.Equal(2, projects[0].GetProperty("builds").GetInt32());
        Assert.Equal(1, projects[0].GetProperty("failedBuilds").GetInt32());
        Assert.Equal(140, projects[0].GetProperty("totalMs").GetDouble());
        Assert.Equal(100, projects[0].GetProperty("maxMs").GetDouble());
        Assert.Equal(60, projects[1].GetProperty("totalMs").GetDouble());
    }

    [Fact]
    public void Partial_replay_preserves_completed_rows_and_does_not_invent_a_negative_remainder()
    {
        var timings = new DesignTimeBuildTimings();
        timings.Observe(0, Started(1, "Root.csproj", 0));
        timings.Observe(0, Finished(1, "Root.csproj", 100, true));
        timings.Observe(1, Started(1, "Incomplete.csproj", 110));
        timings.Observe(1, Finished(2, "MissingStart.csproj", 120, true));
        using var document = JsonDocument.Parse(timings.ToJson(50, ["replay_failed:1:InvalidDataException"]));
        var root = document.RootElement;
        Assert.Equal("incomplete", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("unattributedOpenMs").ValueKind);
        Assert.Equal("binlog_span_exceeds_open", root.GetProperty("remainderReason").GetString());
        Assert.Equal(1, root.GetProperty("unmatchedStarts").GetInt32());
        Assert.Equal(1, root.GetProperty("unmatchedFinishes").GetInt32());
        Assert.Single(root.GetProperty("projects").EnumerateArray());
        Assert.Single(root.GetProperty("replayFailures").EnumerateArray());
    }

    [Fact]
    public void Empty_capture_is_unavailable()
    {
        using var document = JsonDocument.Parse(new DesignTimeBuildTimings().ToJson(100, []));
        Assert.Equal("unavailable", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("binlogSpanMs").ValueKind);
        Assert.Equal("no_events", document.RootElement.GetProperty("remainderReason").GetString());
    }

    [Fact]
    public void Target_and_task_rankings_are_bounded_and_sorted_by_inclusive_duration()
    {
        var timings = new DesignTimeBuildTimings();
        for (var index = 1; index <= 12; index++)
        {
            var context = new BuildEventContext(1, index, 1, index);
            timings.Observe(0, new TargetStartedEventArgs("Started", "", "Target" + index,
                "Root.csproj", "Root.targets", "", At(0)) { BuildEventContext = context });
            timings.Observe(0, new TaskStartedEventArgs("Started", "", "Root.csproj", "Root.targets",
                "Task" + index, At(0)) { BuildEventContext = context });
            timings.Observe(0, new TaskFinishedEventArgs("Finished", "", "Root.csproj", "Root.targets",
                "Task" + index, true, At(index)) { BuildEventContext = context });
            timings.Observe(0, new TargetFinishedEventArgs("Finished", "", "Target" + index,
                "Root.csproj", "Root.targets", true, At(index * 2), null!) { BuildEventContext = context });
        }

        using var document = JsonDocument.Parse(timings.ToJson(100, []));
        var targets = document.RootElement.GetProperty("targets").EnumerateArray().ToArray();
        var tasks = document.RootElement.GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Equal(10, targets.Length);
        Assert.Equal(10, tasks.Length);
        Assert.Equal("Target12", targets[0].GetProperty("name").GetString());
        Assert.Equal(24, targets[0].GetProperty("totalMs").GetDouble());
        Assert.Equal("Task12", tasks[0].GetProperty("name").GetString());
        Assert.Equal(12, tasks[0].GetProperty("totalMs").GetDouble());
    }

    [Fact]
    public void Negative_interval_is_rejected()
    {
        var timings = new DesignTimeBuildTimings();
        timings.Observe(0, Started(1, "Root.csproj", 100));
        timings.Observe(0, Finished(1, "Root.csproj", 0, true));
        using var document = JsonDocument.Parse(timings.ToJson(150, []));
        Assert.Equal("incomplete", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("invalidIntervals").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("projects").EnumerateArray());
    }

    private static ProjectStartedEventArgs Started(int id, string path, int milliseconds) =>
        new("Project started", "", path, "Compile", null!, null!, At(milliseconds))
        {
            BuildEventContext = new BuildEventContext(1, 1, id, 1),
        };

    private static ProjectFinishedEventArgs Finished(int id, string path, int milliseconds, bool succeeded) =>
        new("Project finished", "", path, succeeded, At(milliseconds))
        {
            BuildEventContext = new BuildEventContext(1, 1, id, 1),
        };

    private static DateTime At(int milliseconds) => new DateTime(2026, 10, 22, 0, 0, 0).AddMilliseconds(milliseconds);
}
