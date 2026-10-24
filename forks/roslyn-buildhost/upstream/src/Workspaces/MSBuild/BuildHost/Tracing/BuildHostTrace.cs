// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Microsoft.CodeAnalysis.MSBuild;

/// <summary>
/// Opt-in timing trace of the build host. When the environment variable <see cref="DirectoryVariable"/> names a
/// directory, every span is written as one JSON line to <c>buildhost-{process id}.jsonl</c> in it. Without the
/// variable <see cref="Start"/> returns <see langword="null"/>, no span is created and nothing is written; the RPC
/// server still reads <see cref="Timestamp"/> once per request.
/// </summary>
/// <remarks>
/// <para>Timestamps are <see cref="Stopwatch.GetTimestamp"/> values. On Windows (QueryPerformanceCounter) and on
/// Linux (CLOCK_MONOTONIC) that clock is shared by all processes of the machine, so the client can place these spans
/// between its own timestamps; the start line records the frequency.</para>
/// <para>The trace never changes what the host does or answers. A failure to create or write the file turns the
/// trace off for the rest of the process and is reported once on stderr: thrown from a span's Dispose inside
/// RpcServer.ProcessRequestAsync, it would turn a finished request into an error response, and thrown from
/// <see cref="Initialize"/>, it would stop the host before the client connects.</para>
/// <para>Only the .NET (netcore) host of this fork is built and traced. The .NET Framework host would run the RPC
/// server in the default AppDomain and the build host in a separate one that is not initialized here, so it would
/// write RPC spans without the evaluation and build spans under them.</para>
/// </remarks>
internal static class BuildHostTrace
{
    public const string DirectoryVariable = "ROSLYN_BUILDHOST_TRACE_DIRECTORY";

    private static readonly object s_gate = new();
    private static readonly AsyncLocal<Span?> s_current = new();
    private static StreamWriter? s_writer;
    private static int s_nextId;

    public static long Timestamp => Stopwatch.GetTimestamp();

    public static void Initialize()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrEmpty(directory))
            return;

        StreamWriter? writer = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            Directory.CreateDirectory(directory);
            writer = new StreamWriter(Path.Combine(directory, $"buildhost-{process.Id}.jsonl"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            WriteLine(writer, json =>
            {
                json.WriteString("event", "start");
                json.WriteNumber("processId", process.Id);
                json.WriteNumber("frequency", Stopwatch.Frequency);
                json.WriteNumber("timestamp", Stopwatch.GetTimestamp());
                json.WriteString("utc", DateTime.UtcNow);
                json.WriteString("processStartUtc", process.StartTime.ToUniversalTime());
                json.WriteString("runtime", RuntimeInformation.FrameworkDescription);
            });

            s_writer = writer;
        }
        catch (Exception e)
        {
            if (writer is not null)
                CloseQuietly(writer);

            ReportFailure(e);
        }
    }

    /// <summary>
    /// Starts a span nested in the current one of this async flow, or returns <see langword="null"/> when tracing is off.
    /// </summary>
    public static Span? Start(string kind, string? path = null)
    {
        if (s_writer is null)
            return null;

        var parent = s_current.Value;
        var span = new Span(Interlocked.Increment(ref s_nextId), parent, kind, path);
        s_current.Value = span;
        return span;
    }

    private static void WriteLine(StreamWriter writer, Action<Utf8JsonWriter> writeProperties)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            writeProperties(json);
            json.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(stream.ToArray());
        lock (s_gate)
        {
            writer.WriteLine(line);
        }
    }

    private static void WriteSpanLine(StreamWriter writer, Action<Utf8JsonWriter> writeProperties)
    {
        try
        {
            WriteLine(writer, writeProperties);
        }
        catch (Exception e)
        {
            // Only the first failure turns the trace off and reports it; a span closing on another thread at the same
            // time finds either no writer or a closed one, and its own failure stops here.
            if (Interlocked.CompareExchange(ref s_writer, null, writer) != writer)
                return;

            ReportFailure(e);
            CloseQuietly(writer);
        }
    }

    private static void CloseQuietly(StreamWriter writer)
    {
        try
        {
            writer.Dispose();
        }
        catch (Exception)
        {
            // The trace is already off; failing to flush the rest of it changes nothing for the host.
        }
    }

    private static void ReportFailure(Exception e)
    {
        try
        {
            Console.Error.WriteLine($"BuildHost trace turned off: {e.GetType().Name}: {e.Message}");
        }
        catch (Exception)
        {
            // stderr is the host's log channel; a trace failure must not become a host failure through it either.
        }
    }

    internal sealed class Span : IDisposable
    {
        private readonly int _id;
        private readonly Span? _parent;
        private readonly string _kind;
        private readonly string? _path;
        private readonly long _start;
        private readonly int _threadId;
        private readonly List<KeyValuePair<string, object>> _data = [];
        private bool _disposed;

        public Span(int id, Span? parent, string kind, string? path)
        {
            _id = id;
            _parent = parent;
            _kind = kind;
            _path = path;
            _threadId = Environment.CurrentManagedThreadId;
            _start = Stopwatch.GetTimestamp();
        }

        public void Set(string name, string? value)
        {
            if (value is not null)
                _data.Add(new(name, value));
        }

        public void Set(string name, long value)
            => _data.Add(new(name, value));

        public void Set(string name, bool value)
            => _data.Add(new(name, value));

        public void Set(string name, string[] values)
            => _data.Add(new(name, values));

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            var end = Stopwatch.GetTimestamp();
            s_current.Value = _parent;

            var writer = s_writer;
            if (writer is null)
                return;

            WriteSpanLine(writer, json =>
            {
                json.WriteString("event", "span");
                json.WriteNumber("id", _id);
                json.WriteNumber("parent", _parent?._id ?? 0);
                json.WriteString("kind", _kind);
                if (_path is not null)
                    json.WriteString("path", _path);

                json.WriteNumber("start", _start);
                json.WriteNumber("end", end);
                json.WriteNumber("thread", _threadId);
                foreach (var pair in _data)
                {
                    switch (pair.Value)
                    {
                        case string text:
                            json.WriteString(pair.Key, text);
                            break;
                        case long number:
                            json.WriteNumber(pair.Key, number);
                            break;
                        case bool flag:
                            json.WriteBoolean(pair.Key, flag);
                            break;
                        case string[] values:
                            json.WriteStartArray(pair.Key);
                            foreach (var item in values)
                                json.WriteStringValue(item);

                            json.WriteEndArray();
                            break;
                    }
                }
            });
        }
    }
}
