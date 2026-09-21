using System.Collections.Generic;
using System.Text;

namespace RoslynMcpServer.Services;

/// <summary>
/// In-memory overflow for navigation listings that exceed <c>maxResults</c>.
/// Not a host filesystem path — remote clients fetch chunks via <c>overflowCursor</c>.
/// </summary>
internal static class NavigationOverflowStore
{
    public const int MaxEntries = 8;
    public const int MaxTotalChars = 2_000_000;
    public const int ChunkChars = 16_000;
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

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

    public static StoreOutcome TryStore(string remainderText)
    {
        ArgumentNullException.ThrowIfNull(remainderText);

        if (remainderText.Length == 0)
        {
            return StoreOutcome.Empty();
        }

        if (remainderText.Length > MaxTotalChars)
        {
            return StoreOutcome.TooLarge(remainderText.Length);
        }

        lock (Gate)
        {
            EvictExpiredUnlocked();

            // Evict oldest until we have an open slot and enough char budget.
            while (Order.Count > 0
                   && (Order.Count >= MaxEntries || TotalCharsUnlocked() + remainderText.Length > MaxTotalChars))
            {
                RemoveNode(Order.First!);
            }

            // After eviction, a single payload that still does not fit should not be stored.
            if (remainderText.Length > MaxTotalChars
                || TotalCharsUnlocked() + remainderText.Length > MaxTotalChars)
            {
                return StoreOutcome.TooLarge(remainderText.Length);
            }

            var id = Guid.NewGuid().ToString("N");
            var entry = new Entry(id, remainderText, _timeProvider.GetUtcNow());
            var node = Order.AddLast(entry);
            ById[id] = node;
            return StoreOutcome.Stored(id);
        }
    }

    public static TakeOutcome TryTakeChunk(string? cursorId)
    {
        if (string.IsNullOrWhiteSpace(cursorId))
        {
            return TakeOutcome.FromError(
                "Error: `overflowCursor` is empty. Pass the cursor id returned by a previous navigation listing.");
        }

        var id = cursorId.Trim();
        lock (Gate)
        {
            EvictExpiredUnlocked();

            if (!ById.TryGetValue(id, out var node))
            {
                return TakeOutcome.FromError(
                    $"Error: unknown or expired `overflowCursor` `{id}`. Re-run the search, or narrow the query.");
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

    private static int TotalCharsUnlocked()
    {
        var total = 0;
        foreach (var entry in Order)
        {
            total += entry.Remaining.Length;
        }

        return total;
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
        private StoreOutcome(StoreStatus status, string? cursorId, int attemptedChars)
        {
            Status = status;
            CursorId = cursorId;
            AttemptedChars = attemptedChars;
        }

        public StoreStatus Status { get; }
        public string? CursorId { get; }
        public int AttemptedChars { get; }

        public static StoreOutcome Stored(string cursorId) => new(StoreStatus.Stored, cursorId, 0);
        public static StoreOutcome TooLarge(int attemptedChars) => new(StoreStatus.TooLarge, null, attemptedChars);
        public static StoreOutcome Empty() => new(StoreStatus.Empty, null, 0);
    }

    internal enum StoreStatus
    {
        Stored,
        TooLarge,
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
/// Shared maxResults / preview / overflow listing for navigation tools.
/// </summary>
internal static class NavigationListingHelper
{
    public const int DefaultMaxResults = 50;
    public const int MinMaxResults = 1;
    public const int MaxMaxResultsBound = 500;
    public const int PreviewSourceLineChars = 400;
    public const string MaxResultsEnvVariable = "ROSLYN_MCP_MAX_RESULTS";

    /// <summary>
    /// Explicit arg → env <see cref="MaxResultsEnvVariable"/> → optional config <c>max-results</c> → 50.
    /// Clamp 1–500.
    /// </summary>
    public static int ResolveMaxResults(int? maxResults, int? configFallback = null)
    {
        if (maxResults is int explicitValue)
        {
            return Math.Clamp(explicitValue, MinMaxResults, MaxMaxResultsBound);
        }

        var env = Environment.GetEnvironmentVariable(MaxResultsEnvVariable);
        if (int.TryParse(env, out var fromEnv) && fromEnv > 0)
        {
            return Math.Clamp(fromEnv, MinMaxResults, MaxMaxResultsBound);
        }

        if (configFallback is int fromConfig && fromConfig > 0)
        {
            return Math.Clamp(fromConfig, MinMaxResults, MaxMaxResultsBound);
        }

        return DefaultMaxResults;
    }

    /// <summary>Explicit <paramref name="preview"/> wins; otherwise optional config <c>preview</c>; otherwise false.</summary>
    public static bool ResolvePreview(bool? preview, bool? configFallback = null)
        => preview ?? configFallback ?? false;

    public static string FormatLocationLine(string path, int line1Based, int column1Based, string? previewSourceLine)
    {
        var location = $"{path}:{line1Based}:{column1Based}";
        if (previewSourceLine is null)
        {
            return location;
        }

        return $"{location} | `{EscapeMdBackticks(TruncatePreview(previewSourceLine))}`";
    }

    public static string TruncatePreview(string raw)
    {
        var trimmed = raw.TrimEnd();
        if (trimmed.Length <= PreviewSourceLineChars)
        {
            return trimmed;
        }

        return trimmed[..PreviewSourceLineChars] + "…";
    }

    public static string EscapeMdBackticks(string s) => s.Replace('`', '\'');

    /// <summary>
    /// Appends up to <paramref name="maxResults"/> lines, then stores any remainder in
    /// <see cref="NavigationOverflowStore"/> (no silent drop).
    /// </summary>
    public static void AppendCappedLines(
        StringBuilder sb,
        IReadOnlyList<string> lines,
        int maxResults,
        string itemNoun)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(lines);

        maxResults = Math.Clamp(maxResults, MinMaxResults, MaxMaxResultsBound);
        var shown = Math.Min(maxResults, lines.Count);
        for (var i = 0; i < shown; i++)
        {
            sb.AppendLine(lines[i]);
        }

        if (lines.Count <= maxResults)
        {
            return;
        }

        var remainderBuilder = new StringBuilder();
        for (var i = maxResults; i < lines.Count; i++)
        {
            if (remainderBuilder.Length > 0)
            {
                remainderBuilder.Append('\n');
            }

            remainderBuilder.Append(lines[i]);
        }

        var remainder = remainderBuilder.ToString();
        var omitted = lines.Count - maxResults;
        sb.AppendLine();

        var store = NavigationOverflowStore.TryStore(remainder);
        switch (store.Status)
        {
            case NavigationOverflowStore.StoreStatus.Stored:
                sb.AppendLine(
                    $"[!] Showing {shown} of {lines.Count} {itemNoun} (maxResults={maxResults}). "
                    + $"{omitted} more stored in-memory — list is **not complete** until overflow is fetched. "
                    + $"Pass `overflowCursor`=`{store.CursorId}` to the same tool for the next chunk "
                    + $"({NavigationOverflowStore.ChunkChars} chars; TTL {NavigationOverflowStore.Ttl.TotalMinutes:0} min).");
                break;
            case NavigationOverflowStore.StoreStatus.TooLarge:
                sb.AppendLine(
                    $"[!] Showing {shown} of {lines.Count} {itemNoun} (maxResults={maxResults}). "
                    + $"The remainder ({store.AttemptedChars} chars) was too large to store "
                    + $"(cap {NavigationOverflowStore.MaxTotalChars} chars). Narrow the query "
                    + "(FQN / `filePath` / lower `maxResults` is not enough — reduce matches).");
                break;
            case NavigationOverflowStore.StoreStatus.Empty:
                break;
        }
    }

    public static string FormatOverflowChunkResponse(NavigationOverflowStore.TakeOutcome take)
    {
        if (!take.Ok)
        {
            return take.Error!;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Navigation overflow chunk");
        sb.AppendLine();
        sb.AppendLine(take.Chunk);
        sb.AppendLine();
        if (take.HasMore)
        {
            sb.AppendLine(
                $"[!] More overflow remains. Pass `overflowCursor`=`{take.CursorId}` again for the next chunk.");
        }
        else
        {
            sb.AppendLine("[*] Overflow exhausted; cursor released.");
        }

        return sb.ToString().TrimEnd();
    }
}
