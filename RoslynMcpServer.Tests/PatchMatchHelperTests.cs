using System.Diagnostics;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class PatchMatchHelperTests
{
    [Fact]
    public void ReplaceAll_whenNewStringContainsOldString_doesNotHang_andSkipsInsertedText()
    {
        const string oldString = "AllSoftNotificationHelper";
        const string newString = "HelperContainer.Eis.AllSoftNotificationHelpers";
        const string source =
            "var x = AllSoftNotificationHelper.Create();\n" +
            "AllSoftNotificationHelper y = x;";

        var sw = Stopwatch.StartNew();
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            source,
            oldString,
            newString,
            replaceAll: true,
            out var usedFlexible,
            out var matched,
            out var replacementCount);
        sw.Stop();

        Assert.True(matched);
        Assert.False(usedFlexible);
        Assert.Equal(2, replacementCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"replaceAll took {sw.Elapsed}; likely a matcher loop");
        Assert.Equal(
            "var x = HelperContainer.Eis.AllSoftNotificationHelpers.Create();\n" +
            "HelperContainer.Eis.AllSoftNotificationHelpers y = x;",
            updated);
        Assert.DoesNotContain("HelperContainer.Eis.HelperContainer", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceAll_exact_replacesEveryOccurrence()
    {
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "foo bar foo",
            "foo",
            "baz",
            replaceAll: true,
            out _,
            out var matched,
            out var replacementCount);

        Assert.True(matched);
        Assert.Equal(2, replacementCount);
        Assert.Equal("baz bar baz", updated);
    }

    [Fact]
    public void ReplaceFirst_leavesLaterOccurrences()
    {
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "foo bar foo",
            "foo",
            "baz",
            replaceAll: false,
            out _,
            out var matched,
            out var replacementCount);

        Assert.True(matched);
        Assert.Equal(1, replacementCount);
        Assert.Equal("baz bar foo", updated);
    }

    [Fact]
    public void ReplaceAll_emptyNewString_deletesOccurrences()
    {
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "abXabXab",
            "X",
            string.Empty,
            replaceAll: true,
            out _,
            out var matched,
            out var replacementCount);

        Assert.True(matched);
        Assert.Equal(2, replacementCount);
        Assert.Equal("ababab", updated);
    }

    [Fact]
    public void ReplaceAll_whitespaceSeparatedNeedle_whenNewContainsOld_doesNotHang()
    {
        var sw = Stopwatch.StartNew();
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "hello   world  hello   world",
            "hello world",
            "ns.hello world",
            replaceAll: true,
            out _,
            out var matched,
            out var replacementCount);
        sw.Stop();

        Assert.True(matched);
        Assert.Equal(2, replacementCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"replaceAll took {sw.Elapsed}; likely a matcher loop");
        Assert.Equal("ns.hello world  ns.hello world", updated);
    }

    [Fact]
    public void ReplaceAll_respectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PatchMatchHelper.ApplyPatchWithFlexibleFallback(
                "foo foo",
                "foo",
                "bar",
                replaceAll: true,
                out _,
                out _,
                out _,
                cts.Token));
    }

    [Fact]
    public void ReplaceAll_noMatch_returnsNull()
    {
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "alpha",
            "beta",
            "gamma",
            replaceAll: true,
            out var usedFlexible,
            out var matched,
            out var replacementCount);

        Assert.False(matched);
        Assert.False(usedFlexible);
        Assert.Equal(0, replacementCount);
        Assert.Null(updated);
    }
}
