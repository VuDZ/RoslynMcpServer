using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// In-memory full process logs for truncated / partial / unparsed build-test-run reports.
/// Not a host filesystem path — remote clients fetch chunks via <c>reportCursor</c>.
/// </summary>
internal static class DiagnosticReportStore
{
    public const int MaxEntries = 4;
    public const int MaxReportChars = 1_000_000;
    public const int ChunkChars = 16_000;
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    /// <summary>Appended when a redacted report exceeds <see cref="MaxReportChars"/>.</summary>
    public const string CapMarker = "\n\n...[truncated at cap]...\n";

    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Order = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> ById =
        new(StringComparer.Ordinal);

    private static TimeProvider _timeProvider = TimeProvider.System;

    internal static TimeProvider TimeProviderForTests
    {
        get => _timeProvider;
        set => _timeProvider = value ?? TimeProvider.System;
    }

    /// <summary>Clears all entries and restores the system clock. For unit tests only.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Order.Clear();
            ById.Clear();
            _timeProvider = TimeProvider.System;
        }
    }

    /// <summary>Forces TTL expiry for a cursor without advancing the clock. For unit tests only.</summary>
    internal static bool ExpireForTests(string cursorId)
    {
        if (string.IsNullOrWhiteSpace(cursorId))
        {
            return false;
        }

        lock (Gate)
        {
            if (!ById.TryGetValue(cursorId.Trim(), out var node))
            {
                return false;
            }

            RemoveNode(node);
            return true;
        }
    }

    /// <summary>
    /// Redacts secrets, caps at <see cref="MaxReportChars"/> with an explicit marker, then stores.
    /// Always stores non-empty input (never silent drop for size).
    /// </summary>
    public static StoreOutcome TryStore(string combinedOutput)
    {
        ArgumentNullException.ThrowIfNull(combinedOutput);

        if (combinedOutput.Length == 0)
        {
            return StoreOutcome.Empty();
        }

        var payload = ProcessOutputRedactor.Redact(combinedOutput);
        if (payload.Length > MaxReportChars)
        {
            var prefixLen = MaxReportChars - CapMarker.Length;
            if (prefixLen < 1)
            {
                prefixLen = 1;
            }

            payload = string.Concat(payload.AsSpan(0, prefixLen), CapMarker);
        }

        lock (Gate)
        {
            EvictExpiredUnlocked();

            while (Order.Count >= MaxEntries)
            {
                RemoveNode(Order.First!);
            }

            var id = Guid.NewGuid().ToString("N");
            // Single assignment under the lock is the atomic publish (no half-written file).
            var entry = new Entry(id, payload, _timeProvider.GetUtcNow());
            var node = Order.AddLast(entry);
            ById[id] = node;
            return StoreOutcome.Stored(id, payload.Length);
        }
    }

    public static TakeOutcome TryTakeChunk(string? cursorId)
    {
        if (string.IsNullOrWhiteSpace(cursorId))
        {
            return TakeOutcome.FromError(
                "Error: `reportCursor` is empty. Pass the cursor id returned by a truncated or partial build/test/run report.");
        }

        var id = cursorId.Trim();
        lock (Gate)
        {
            EvictExpiredUnlocked();

            if (!ById.TryGetValue(id, out var node))
            {
                return TakeOutcome.FromError(
                    $"Error: unknown or expired `reportCursor` `{id}`. Re-run the tool, or use the cursor from the latest report.");
            }

            var entry = node.Value;
            var remaining = entry.Remaining;
            var take = Math.Min(ChunkChars, remaining.Length);
            var chunk = remaining[..take];
            var leftover = remaining[take..];

            if (leftover.Length == 0)
            {
                RemoveNode(node);
                return TakeOutcome.FromChunk(chunk, hasMore: false, cursorId: id);
            }

            node.Value = entry with { Remaining = leftover };
            return TakeOutcome.FromChunk(chunk, hasMore: true, cursorId: id);
        }
    }

    private static void EvictExpiredUnlocked()
    {
        var now = _timeProvider.GetUtcNow();
        while (Order.First is { } first)
        {
            if (now - first.Value.CreatedUtc <= Ttl)
            {
                break;
            }

            RemoveNode(first);
        }
    }

    private static void RemoveNode(LinkedListNode<Entry> node)
    {
        ById.Remove(node.Value.Id);
        Order.Remove(node);
    }

    private readonly record struct Entry(string Id, string Remaining, DateTimeOffset CreatedUtc);

    internal readonly struct StoreOutcome
    {
        private StoreOutcome(StoreStatus status, string? cursorId, int storedChars)
        {
            Status = status;
            CursorId = cursorId;
            StoredChars = storedChars;
        }

        public StoreStatus Status { get; }
        public string? CursorId { get; }
        public int StoredChars { get; }

        public static StoreOutcome Stored(string cursorId, int storedChars) =>
            new(StoreStatus.Stored, cursorId, storedChars);

        public static StoreOutcome Empty() => new(StoreStatus.Empty, null, 0);
    }

    internal enum StoreStatus
    {
        Stored,
        Empty,
    }

    internal readonly struct TakeOutcome
    {
        private TakeOutcome(bool ok, string? chunk, bool hasMore, string? cursorId, string? error)
        {
            Ok = ok;
            Chunk = chunk;
            HasMore = hasMore;
            CursorId = cursorId;
            Error = error;
        }

        public bool Ok { get; }
        public string? Chunk { get; }
        public bool HasMore { get; }
        public string? CursorId { get; }
        public string? Error { get; }

        public static TakeOutcome FromChunk(string chunk, bool hasMore, string cursorId) =>
            new(true, chunk, hasMore, cursorId, null);

        public static TakeOutcome FromError(string message) =>
            new(false, null, false, null, message);
    }
}

/// <summary>
/// Redacts common secret assignment / Bearer / URL-userinfo patterns from process logs
/// before they enter <see cref="DiagnosticReportStore"/> or leave via chunks.
/// </summary>
internal static class ProcessOutputRedactor
{
    private static readonly Regex AssignmentSecret = new(
        @"\b(password|pwd|token|secret|apikey|api_key)\b(\s*[:=]\s*)(""[^""]*""|'[^']*'|[^\s""']+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AuthorizationAssignment = new(
        @"\bAuthorization\b(\s*[:=]\s*)[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BearerToken = new(
        @"\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UrlUserInfoPassword = new(
        @"(https?://[^/\s:@\s]+):([^/\s@]+)@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        var redacted = AuthorizationAssignment.Replace(text, static m =>
            "Authorization" + m.Groups[1].Value + "[redacted]");
        redacted = BearerToken.Replace(redacted, "Bearer [redacted]");
        redacted = AssignmentSecret.Replace(redacted, static m =>
            m.Groups[1].Value + m.Groups[2].Value + "[redacted]");
        redacted = UrlUserInfoPassword.Replace(redacted, "$1:[redacted]@");
        return redacted;
    }
}

/// <summary>
/// Decides when to store a full report and formats cursor / chunk responses for build-test-run tools.
/// </summary>
internal static class DiagnosticReportAttachment
{
    public const string ReportCursorParameterDescription =
        "Next full-report chunk; no new process.";

    public static bool ClientResponseHasTruncatedExcerpt(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return false;
        }

        return response.Contains(TruncatedProcessLog.MiddleMarker, StringComparison.Ordinal)
               || response.Contains("\n...[TRUNCATED]...\n", StringComparison.Ordinal)
               || response.Contains("(stderr truncated;", StringComparison.Ordinal)
               || response.Contains(" truncated to ", StringComparison.OrdinalIgnoreCase)
               || response.Contains(" truncated at the ", StringComparison.OrdinalIgnoreCase)
               || response.Contains("_truncated at parse time", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPartialOrUnparsedTestStatus(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return false;
        }

        return response.Contains("**Status:** partial", StringComparison.Ordinal)
               || response.Contains("No standard VSTest/xUnit summary line was detected", StringComparison.Ordinal)
               || response.Contains("Silent / unparsed failure hints", StringComparison.Ordinal);
    }

    public static bool IsUnparsedBuildFailure(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return false;
        }

        return response.Contains(
            "No lines matched MSBuild `path(line,col): error|warning CODE`",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// When <paramref name="shouldStore"/> is true, stores the redacted combined log and appends a cursor line.
    /// </summary>
    public static void AppendCursorLineIfStored(StringBuilder sb, string combinedOutput, bool shouldStore)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (!shouldStore || string.IsNullOrEmpty(combinedOutput))
        {
            return;
        }

        var stored = DiagnosticReportStore.TryStore(combinedOutput);
        if (stored.Status != DiagnosticReportStore.StoreStatus.Stored || stored.CursorId is null)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Full report: pass reportCursor=`{stored.CursorId}` to this same tool.");
    }

    public static string AttachToResponse(string response, string combinedOutput, bool shouldStore)
    {
        if (!shouldStore || string.IsNullOrEmpty(combinedOutput))
        {
            return response;
        }

        var sb = new StringBuilder(response);
        AppendCursorLineIfStored(sb, combinedOutput, shouldStore: true);
        return sb.ToString().TrimEnd();
    }

    public static string FormatChunkResponse(DiagnosticReportStore.TakeOutcome take)
    {
        if (!take.Ok)
        {
            return take.Error!;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Diagnostic report chunk");
        sb.AppendLine();
        // Chunks are already redacted at store time; re-redact as defense in depth.
        sb.AppendLine(ProcessOutputRedactor.Redact(take.Chunk!));
        sb.AppendLine();
        if (take.HasMore)
        {
            sb.AppendLine(
                $"Full report: pass reportCursor=`{take.CursorId}` to this same tool.");
        }
        else
        {
            sb.AppendLine("[*] Full report exhausted; cursor released.");
        }

        return sb.ToString().TrimEnd();
    }
}
