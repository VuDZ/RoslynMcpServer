using System.Text;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Build;

/// <summary>Drains one pipe while retaining a bounded excerpt of complete records.</summary>
internal sealed class BoundedProcessOutput
{
    // Two streams from each of two commands fit under the diagnostic report store cap.
    internal const int MaxStreamChars = 240_000;
    private const int HeadChars = 48_000;
    private const int TailChars = 48_000;
    private const int SummaryChars = 64_000;
    private const int ErrorChars = 32_000;
    private const int WarningChars = 24_000;
    private const int ContextChars = 16_000;

    internal static async Task<Capture> ReadAsync(TextReader reader)
    {
        StringBuilder? whole = new();
        BoundedProcessOutput? bounded = null;
        var buffer = new char[8192];
        long charsRead = 0;
        long trimmedLength = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            var chunk = buffer.AsSpan(0, count);
            var nonWhitespaceLength = chunk.TrimEnd().Length;
            if (nonWhitespaceLength > 0)
            {
                trimmedLength = charsRead + nonWhitespaceLength;
            }

            charsRead += count;
            if (whole is not null && charsRead <= MaxStreamChars)
            {
                whole.Append(chunk);
                continue;
            }

            if (whole is not null)
            {
                // ReadLineAsync can allocate an unbounded string even if the caller discards that line.
                // See docs/project-review-2026-11-25/task-07-p2-bounded-cli-output-hi.md.
                bounded = new BoundedProcessOutput();
                bounded.Append(whole.ToString().AsSpan());
                whole = null;
            }

            bounded!.Append(chunk);
        }

        var length = (int)Math.Min(trimmedLength, int.MaxValue);
        return bounded is null
            ? new Capture(whole!.ToString().TrimEnd(), length)
            : bounded.Finish(length);
    }

    internal static (Capture StdOut, Capture StdErr) ProtectCrossPipeSecrets(Capture stdout, Capture stderr)
    {
        if ((!stdout.Truncated && !stderr.Truncated) || string.IsNullOrEmpty(stderr.Text))
        {
            return (stdout, stderr);
        }

        // Reports concatenate stdout and stderr. A secret can have started in stdout even
        // when the assignment prefix was dropped before stderr's excerpt was assembled.
        var continuation = stdout.Retained?._secretContext.PendingContinuation ?? FindPendingSecret(stdout.Text);
        if (continuation.Kind == MultilineProcessSecret.ContinuationKind.None)
        {
            return (stdout, stderr);
        }

        var leading = stderr.Retained?._leading ?? InspectLeadingOutput(stderr.Text);
        var throughLine = leading.FindContinuationEnd(continuation);
        if (throughLine < 0)
        {
            return (stdout, stderr);
        }

        if (!stdout.Truncated)
        {
            var terminator = continuation.Kind switch
            {
                MultilineProcessSecret.ContinuationKind.QuotedValue => continuation.Quote.ToString(),
                MultilineProcessSecret.ContinuationKind.AssignmentValue or MultilineProcessSecret.ContinuationKind.Bearer
                    => " redacted",
                _ => string.Empty,
            };
            if (terminator.Length != 0)
            {
                stdout = stdout with { Text = ProcessOutputRedactor.Redact(stdout.Text + terminator) };
            }
        }

        if (stderr.Retained is { } retained)
        {
            return (stdout, retained.Render(stderr.Length, throughLine));
        }

        var summaryTruncated = stderr.SummaryTruncated;
        var remainder = OmitLeadingLines(stderr.Text, throughLine, ref summaryTruncated);
        return (stdout, stderr with
        {
            Text = string.Concat(TruncatedProcessLog.CaptureMarker, "\n", remainder).TrimEnd(),
            Truncated = true,
            SummaryTruncated = summaryTruncated,
        });
    }

    private static MultilineProcessSecret.Continuation FindPendingSecret(string text)
    {
        var secret = new MultilineProcessSecret();
        var span = text.AsSpan();
        foreach (var range in span.Split('\n'))
        {
            secret.Scan(span[range]);
            secret.CompleteLine();
        }

        return secret.PendingContinuation;
    }

    private static LeadingProcessOutput InspectLeadingOutput(string text)
    {
        var leading = new LeadingProcessOutput();
        var span = text.AsSpan();
        long line = 0;
        foreach (var range in span.Split('\n'))
        {
            leading.Scan(span[range], line++);
        }

        return leading;
    }

    private static string OmitLeadingLines(string text, long throughLine, ref bool summaryTruncated)
    {
        var remaining = text.AsSpan();
        while (throughLine >= 0 && !remaining.IsEmpty)
        {
            var newline = remaining.IndexOf('\n');
            var count = newline < 0 ? remaining.Length : newline + 1;
            summaryTruncated |= VstestOutputParser.IsSummaryCaptureLine(remaining[..count].Trim());
            remaining = remaining[count..];
            throughLine--;
        }

        return remaining.ToString();
    }

    private void Append(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            var newline = text.IndexOf('\n');
            var count = newline < 0 ? text.Length : newline + 1;
            var content = newline < 0 ? text : text[..newline];
            _leading.Scan(content, _lineNumber);
            _secretContext.Scan(content);
            if (!_lineTooLong)
            {
                if (count <= MaxStreamChars - _line.Length)
                {
                    _line.Append(text[..count]);
                }
                else
                {
                    _lineTooLong = true;
                }
            }

            text = text[count..];
            if (newline >= 0)
            {
                CompleteLine();
            }
        }
    }

    private void CompleteLine()
    {
        var offset = _lineNumber++;
        var sensitiveContinuation = _secretContext.CompleteLine();
        if (_lineTooLong || sensitiveContinuation)
        {
            // Truncated records and multiline secrets may be recognizable only in the omitted context.
            _summaryTruncated |= VstestOutputParser.IsSummaryCaptureLine(_line.ToString().AsSpan());
        }
        else if (_line.Length > 0)
        {
            Retain(new Line(offset, _line.ToString()));
        }

        _line.Clear();
        _lineTooLong = false;
    }

    private void Retain(Line line)
    {
        if (line.Number == _nextHeadLine && line.Text.Length <= HeadChars - _head.Length)
        {
            _head.Append(line.Text);
            _nextHeadLine++;
            return;
        }

        RetainPriorityLine(line);
        // Keep a contiguous suffix. A line larger than the entire tail cannot be part of it.
        if (line.Text.Length > TailChars)
        {
            _tail.Clear();
            _tailLength = 0;
            return;
        }

        while (_tailLength + line.Text.Length > TailChars)
        {
            _tailLength -= _tail.Dequeue().Text.Length;
        }

        _tail.Enqueue(line);
        _tailLength += line.Text.Length;
    }

    private void RetainPriorityLine(Line line)
    {
        var text = line.Text.AsSpan().Trim();
        if (VstestOutputParser.IsSummaryCaptureLine(text))
        {
            _summaryTruncated |= !TryRetainPriorityLine(line, SummaryChars, ref _summaryLength);
        }
        else
        {
            RetainDiagnosticLine(line, text);
        }
    }

    private void RetainDiagnosticLine(Line line, ReadOnlySpan<char> text)
    {
        var severity = BuildOutputReport.GetDiagnosticSeverityForCapture(line.Text);
        if (severity == "error")
        {
            TryRetainPriorityLine(line, ErrorChars, ref _errorLength);
        }
        else if (severity == "warning")
        {
            TryRetainPriorityLine(line, WarningChars, ref _warningLength);
        }
        else if (text.StartsWith("MSBuild executable path", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" -- FAILED", StringComparison.Ordinal)
            || text.Contains("[FAIL]", StringComparison.Ordinal)
            || text.StartsWith("Failed ", StringComparison.Ordinal)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            TryRetainPriorityLine(line, ContextChars, ref _contextLength);
        }
    }

    private bool TryRetainPriorityLine(Line line, int budget, ref int retainedLength)
    {
        if (line.Text.Length > budget - retainedLength)
        {
            return false;
        }

        _priority.Add(line);
        retainedLength += line.Text.Length;
        return true;
    }

    private Capture Finish(int length)
    {
        CompleteLine();
        return Render(length, omitThroughLine: -1);
    }

    private Capture Render(int length, long omitThroughLine)
    {
        var summaryTruncated = _summaryTruncated;
        var head = _head.ToString();
        if (omitThroughLine >= 0)
        {
            head = OmitLeadingLines(head, omitThroughLine, ref summaryTruncated);
        }

        var output = new StringBuilder(head);
        output.AppendLine().AppendLine(TruncatedProcessLog.CaptureMarker).AppendLine();
        var tailStart = _tail.TryPeek(out var firstTailLine) ? firstTailLine.Number : long.MaxValue;
        foreach (var line in _priority)
        {
            if (line.Number >= tailStart)
            {
                break;
            }

            AppendRetainedLine(line);
        }

        foreach (var line in _tail)
        {
            AppendRetainedLine(line);
        }

        return new Capture(output.ToString().TrimEnd(), length, Truncated: true, SummaryTruncated: summaryTruncated)
        {
            Retained = this,
        };

        void AppendRetainedLine(Line line)
        {
            if (line.Number > omitThroughLine)
            {
                output.Append(line.Text);
            }
            else
            {
                summaryTruncated |= VstestOutputParser.IsSummaryCaptureLine(line.Text.AsSpan().Trim());
            }
        }
    }

    internal readonly record struct Capture(
        string Text,
        int Length,
        bool Truncated = false,
        bool SummaryTruncated = false)
    {
        internal BoundedProcessOutput? Retained { get; init; }
    }

    private readonly record struct Line(long Number, string Text);

    private readonly MultilineProcessSecret _secretContext = new();
    private readonly LeadingProcessOutput _leading = new();
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _head = new();
    private readonly Queue<Line> _tail = new();
    private readonly List<Line> _priority = [];
    private long _lineNumber;
    private long _nextHeadLine;
    private bool _lineTooLong;
    private bool _summaryTruncated;
    private int _tailLength;
    private int _summaryLength;
    private int _errorLength;
    private int _warningLength;
    private int _contextLength;
}
