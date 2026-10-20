using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using System.IO.Compression;

namespace ParallelLoad;

internal static class ProfileReplayChecks
{
    public static void Run()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new BuildEventArgs[]
        {
            new BuildStartedEventArgs("Synthetic build", "", start),
            Started(1, start.AddMilliseconds(10)), Finished(1, start.AddMilliseconds(30)),
            Started(2, start.AddMilliseconds(50)), Finished(2, start.AddMilliseconds(80)),
        };
        var replay = new ProjectCostReplay(Environment.CurrentDirectory);
        replay.ReadEvents("synthetic.binlog", handler => Array.ForEach(events, handler), () => 26);
        var session = replay.Sessions.Single();
        Require(session.FirstMs == 30 && session.WeightMs == 50 && session.FirstMs + session.WeightMs == 80,
            "Gaps and first must cover the interval from BuildStarted to the final project end.");
        RequireRejected(events, "ErrorType=UnknownEventType; RecordKind=123");
        RequireRejected([events[0], events[1], events[2], Started(2, start.AddMilliseconds(20)), events[4]]);
        var empty = new ProjectCostReplay(Environment.CurrentDirectory);
        empty.ReadEvents("empty-newer.binlog", handler => handler(events[0]), () => 27);
        Require(empty.Sessions.Single() is { FormatVersion: 27, TopLevelBuilds: 0, WeightMs: 0, FirstMs: 0 },
            "A newer empty BuildHost session must not contribute a project cost.");
        CheckFiles(events);
        Console.WriteLine("Replay checks passed: gap identity, overlap rejection, recoverable error rejection, empty session.");
    }

    private static void CheckFiles(BuildEventArgs[] events)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProfileReplayChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var empty = Path.Combine(directory, "empty.binlog");
            WriteBinlog(empty, [events[0]], unknownRecord: false);
            var replay = new ProjectCostReplay(Environment.CurrentDirectory);
            replay.Read(empty);
            Require(replay.Sessions.Single().TopLevelBuilds == 0, "An actual newer empty binlog must replay.");
            WriteBinlog(empty, [events[0]], unknownRecord: true);
            var emptyWithError = new ProjectCostReplay(Environment.CurrentDirectory);
            emptyWithError.Read(empty);
            Require(emptyWithError.Sessions.Single().RecoverableErrors.Length == 1,
                "A recoverable error without project events must be recorded in the empty session.");

            var sequential = Path.Combine(directory, "sequential.binlog");
            WriteBinlog(sequential, events, unknownRecord: false);
            var sequenceReplay = new ProjectCostReplay(Environment.CurrentDirectory);
            sequenceReplay.Read(sequential);
            Require(sequenceReplay.Sessions.Single() is { FirstMs: 30, WeightMs: 50 },
                "An actual sequential binlog must preserve the synthetic gap identity.");

            var overlap = Path.Combine(directory, "overlap.binlog");
            WriteBinlog(overlap, [events[0], events[1], events[2], Started(2, events[0].Timestamp.AddMilliseconds(20)), events[4]],
                unknownRecord: false);
            RequireFileRejected(overlap, "Top-level builds overlap");

            var damaged = Path.Combine(directory, "recoverable.binlog");
            WriteBinlog(damaged, events, unknownRecord: true);
            RequireFileRejected(damaged, "ErrorType=UnknownEventType; RecordKind=");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RequireFileRejected(string path, string expected)
    {
        try
        {
            new ProjectCostReplay(Environment.CurrentDirectory).Read(path);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(expected, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("An invalid actual binlog must fail with the expected reason.");
    }

    private static void WriteBinlog(string path, BuildEventArgs[] events, bool unknownRecord)
    {
        var source = new SyntheticEventSource();
        var logger = new BinaryLogger { Parameters = path + ";ProjectImports=None" };
        logger.Initialize(source);
        foreach (var args in events)
        {
            source.Raise(args);
        }
        logger.Shutdown();

        byte[] contents;
        using (var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            contents = buffer.ToArray();
        }
        // The public logger emits length-prefixed records. A future unknown record must be recoverable,
        // while the unchanged known project events still make the profile unsafe to use.
        BitConverter.GetBytes(BitConverter.ToInt32(contents) + 1).CopyTo(contents, 0);
        Require(contents[^1] == 0, "The generated binlog must end with EndOfFile.");
        using var output = new GZipStream(File.Create(path), CompressionLevel.Fastest);
        output.Write(contents.AsSpan(0, contents.Length - 1));
        if (unknownRecord)
        {
            output.Write([0xfe, 0x01, 0x00]); // Unknown record kind 254, zero payload length.
        }
        output.WriteByte(0);
    }

    private static ProjectStartedEventArgs Started(int id, DateTime timestamp) =>
        new("Synthetic project", "", Path.Combine(Environment.CurrentDirectory, $"Project{id}.csproj"), "Build", null!, null!, timestamp)
        {
            BuildEventContext = new BuildEventContext(1, 1, id, 1),
        };

    private static ProjectFinishedEventArgs Finished(int id, DateTime timestamp) =>
        new("Synthetic finish", "", Path.Combine(Environment.CurrentDirectory, $"Project{id}.csproj"), true, timestamp)
        {
            BuildEventContext = new BuildEventContext(1, 1, id, 1),
        };

    private static void RequireRejected(BuildEventArgs[] events, params string[] errors)
    {
        try
        {
            new ProjectCostReplay(Environment.CurrentDirectory).ReadEvents("invalid.binlog",
                handler => Array.ForEach(events, handler), () => 27, errors);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("An invalid project-bearing session must fail.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SyntheticEventSource : IEventSource
    {
        public event AnyEventHandler? AnyEventRaised;
        public event BuildStartedEventHandler BuildStarted
        {
            add
            {
            }
            remove
            {
            }
        }

        public event BuildFinishedEventHandler BuildFinished
        {
            add
            {
            }
            remove
            {
            }
        }

        public event ProjectStartedEventHandler ProjectStarted
        {
            add
            {
            }
            remove
            {
            }
        }

        public event ProjectFinishedEventHandler ProjectFinished
        {
            add
            {
            }
            remove
            {
            }
        }

        public event TargetStartedEventHandler TargetStarted
        {
            add
            {
            }
            remove
            {
            }
        }

        public event TargetFinishedEventHandler TargetFinished
        {
            add
            {
            }
            remove
            {
            }
        }

        public event TaskStartedEventHandler TaskStarted
        {
            add
            {
            }
            remove
            {
            }
        }

        public event TaskFinishedEventHandler TaskFinished
        {
            add
            {
            }
            remove
            {
            }
        }

        public event BuildMessageEventHandler MessageRaised
        {
            add
            {
            }
            remove
            {
            }
        }

        public event BuildWarningEventHandler WarningRaised
        {
            add
            {
            }
            remove
            {
            }
        }

        public event BuildErrorEventHandler ErrorRaised
        {
            add
            {
            }
            remove
            {
            }
        }

        public event CustomBuildEventHandler CustomEventRaised
        {
            add
            {
            }
            remove
            {
            }
        }

        public event BuildStatusEventHandler StatusEventRaised
        {
            add
            {
            }
            remove
            {
            }
        }
        public void Raise(BuildEventArgs args) => AnyEventRaised?.Invoke(this, args);
    }
}
