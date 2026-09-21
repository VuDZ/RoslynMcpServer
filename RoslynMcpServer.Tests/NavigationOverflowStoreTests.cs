using System.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class NavigationOverflowStoreTests : IDisposable
{
    public NavigationOverflowStoreTests()
    {
        NavigationOverflowStore.ResetForTests();
    }

    public void Dispose()
    {
        NavigationOverflowStore.ResetForTests();
    }

    [Fact]
    public void TryStore_then_TryTakeChunk_returns_tail_and_exhaustion_errors()
    {
        var payload = new string('a', NavigationOverflowStore.ChunkChars + 25);
        var stored = NavigationOverflowStore.TryStore(payload);
        Assert.Equal(NavigationOverflowStore.StoreStatus.Stored, stored.Status);
        Assert.False(string.IsNullOrWhiteSpace(stored.CursorId));

        var first = NavigationOverflowStore.TryTakeChunk(stored.CursorId);
        Assert.True(first.Ok);
        Assert.True(first.HasMore);
        Assert.Equal(NavigationOverflowStore.ChunkChars, first.Chunk!.Length);
        Assert.Equal(new string('a', NavigationOverflowStore.ChunkChars), first.Chunk);

        var second = NavigationOverflowStore.TryTakeChunk(stored.CursorId);
        Assert.True(second.Ok);
        Assert.False(second.HasMore);
        Assert.Equal(new string('a', 25), second.Chunk);

        var third = NavigationOverflowStore.TryTakeChunk(stored.CursorId);
        Assert.False(third.Ok);
        Assert.Contains("unknown or expired", third.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpireForTests_drops_entry()
    {
        var stored = NavigationOverflowStore.TryStore("remainder-body");
        Assert.Equal(NavigationOverflowStore.StoreStatus.Stored, stored.Status);
        Assert.True(NavigationOverflowStore.ExpireForTests(stored.CursorId!));

        var take = NavigationOverflowStore.TryTakeChunk(stored.CursorId);
        Assert.False(take.Ok);
        Assert.Contains("unknown or expired", take.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oversize_single_payload_is_refused()
    {
        var huge = new string('x', NavigationOverflowStore.MaxTotalChars + 1);
        var stored = NavigationOverflowStore.TryStore(huge);
        Assert.Equal(NavigationOverflowStore.StoreStatus.TooLarge, stored.Status);
        Assert.Null(stored.CursorId);
        Assert.Equal(huge.Length, stored.AttemptedChars);
    }

    [Fact]
    public void Unknown_cursor_returns_human_error()
    {
        var take = NavigationOverflowStore.TryTakeChunk("deadbeefcafebabe");
        Assert.False(take.Ok);
        Assert.StartsWith("Error:", take.Error);
    }
}

public sealed class NavigationListingHelperTests : IDisposable
{
    public NavigationListingHelperTests()
    {
        NavigationOverflowStore.ResetForTests();
    }

    public void Dispose()
    {
        NavigationOverflowStore.ResetForTests();
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, null);
    }

    [Fact]
    public void ResolveMaxResults_arg_wins_over_env_and_clamps()
    {
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, "100");
        Assert.Equal(7, NavigationListingHelper.ResolveMaxResults(7));
        Assert.Equal(1, NavigationListingHelper.ResolveMaxResults(0));
        Assert.Equal(500, NavigationListingHelper.ResolveMaxResults(9999));
    }

    [Fact]
    public void ResolveMaxResults_uses_env_when_arg_omitted()
    {
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, "42");
        Assert.Equal(42, NavigationListingHelper.ResolveMaxResults(null));
    }

    [Fact]
    public void ResolveMaxResults_defaults_to_50()
    {
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, null);
        Assert.Equal(50, NavigationListingHelper.ResolveMaxResults(null));
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, "not-a-number");
        Assert.Equal(50, NavigationListingHelper.ResolveMaxResults(null));
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, "0");
        Assert.Equal(50, NavigationListingHelper.ResolveMaxResults(null));
        Environment.SetEnvironmentVariable(NavigationListingHelper.MaxResultsEnvVariable, "-3");
        Assert.Equal(50, NavigationListingHelper.ResolveMaxResults(null));
    }

    [Fact]
    public void FormatLocationLine_preview_false_omits_source_text()
    {
        var line = NavigationListingHelper.FormatLocationLine(@"C:\src\A.cs", 10, 4, previewSourceLine: null);
        Assert.Equal(@"C:\src\A.cs:10:4", line);
        Assert.DoesNotContain("|", line);
    }

    [Fact]
    public void FormatLocationLine_preview_true_includes_source_line()
    {
        var line = NavigationListingHelper.FormatLocationLine(@"C:\src\A.cs", 10, 4, "    DoWork();");
        Assert.Equal(@"C:\src\A.cs:10:4 | `    DoWork();`", line);
    }

    [Fact]
    public void AppendCappedLines_over_maxResults_mentions_cursor_and_incomplete()
    {
        var lines = Enumerable.Range(1, 5).Select(i => $"loc-{i}").ToList();
        var sb = new StringBuilder();
        sb.AppendLine("header");
        NavigationListingHelper.AppendCappedLines(sb, lines, maxResults: 2, "location(s)");
        var text = sb.ToString();

        Assert.Contains("loc-1", text);
        Assert.Contains("loc-2", text);
        Assert.DoesNotContain("loc-3", text);
        Assert.Contains("overflowCursor", text);
        Assert.Contains("not complete", text);
        Assert.DoesNotContain("Truncated to protect", text);

        var cursorStart = text.IndexOf("overflowCursor`=`", StringComparison.Ordinal);
        Assert.True(cursorStart >= 0);
        var idStart = cursorStart + "overflowCursor`=`".Length;
        var idEnd = text.IndexOf('`', idStart);
        var cursor = text[idStart..idEnd];

        var take = NavigationOverflowStore.TryTakeChunk(cursor);
        Assert.True(take.Ok);
        Assert.Contains("loc-3", take.Chunk);
        Assert.Contains("loc-5", take.Chunk);
    }

    [Fact]
    public void AppendCappedLines_definition_locations_cursor_returns_tail_without_research()
    {
        var lines = Enumerable.Range(1, 5)
            .Select(i =>
                $"Symbol: Type{i}\n"
                + $"  Full name: Ns.Type{i}\n"
                + $"  File: C:\\src\\Type{i}.cs\n"
                + $"  Line: {i}\n"
                + "  Column: 1")
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Found 5 declaration symbol(s) matching `Type`:");
        sb.AppendLine();
        NavigationListingHelper.AppendCappedLines(sb, lines, maxResults: 2, "location(s)");
        var text = sb.ToString();

        Assert.Contains("Type1", text);
        Assert.Contains("Type2", text);
        Assert.DoesNotContain("Type3", text);
        Assert.Contains("overflowCursor", text);
        Assert.Contains("not complete", text);
        Assert.DoesNotContain("Narrow the symbol name", text);
        Assert.DoesNotContain("Output truncated after", text);

        var cursorStart = text.IndexOf("overflowCursor`=`", StringComparison.Ordinal);
        Assert.True(cursorStart >= 0);
        var idStart = cursorStart + "overflowCursor`=`".Length;
        var idEnd = text.IndexOf('`', idStart);
        var cursor = text[idStart..idEnd];

        // Taking the cursor returns the stored remainder only — no declaration search.
        var take = NavigationOverflowStore.TryTakeChunk(cursor);
        Assert.True(take.Ok);
        Assert.False(take.HasMore);
        Assert.Contains("Type3", take.Chunk);
        Assert.Contains("Type5", take.Chunk);
        Assert.Contains("Full name: Ns.Type4", take.Chunk);
        Assert.DoesNotContain("Type1", take.Chunk);
        Assert.DoesNotContain("Type2", take.Chunk);
    }
}
