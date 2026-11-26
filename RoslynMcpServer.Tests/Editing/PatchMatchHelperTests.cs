using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Editing;

public sealed class PatchMatchHelperTests
{
    [Theory]
    [InlineData("Y", "Patch applied successfully")]
    [InlineData("X", "No changes were applied.")]
    public async Task ApplyPatch_preserves_mixed_line_endings_outside_the_requested_change(
        string replacement,
        string expectedStatus)
    {
        const string Source = "var keep = @\"A\nB\";\r\nvar edit = \"X\";\r\n// tail\r";
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, Source);
            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
            var result = await tool.ApplyPatch(path, "var edit = \"X\";", $"var edit = \"{replacement}\";");

            Assert.Contains(expectedStatus, result, StringComparison.Ordinal);
            var expected = $"var keep = @\"A\nB\";\r\nvar edit = \"{replacement}\";\r\n// tail\r";
            Assert.Equal(Encoding.UTF8.GetBytes(expected), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplaceAll_preserves_line_endings_between_exact_and_flexible_matches()
    {
        const string Source = "Start\r\nMarker\nuntouched\rStart\tMarker\r\ntrailer\n";
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            Source,
            "Start\nMarker",
            "New\nStart",
            replaceAll: true,
            out var usedFlexible,
            out var matched,
            out var replacementCount,
            out _);

        Assert.True(matched);
        Assert.True(usedFlexible);
        Assert.Equal(2, replacementCount);
        Assert.Equal("New\r\nStart\nuntouched\rNew\r\nStart\r\ntrailer\n", updated);
    }

    [Fact]
    public void ReplaceAll_does_not_revisit_flexible_matches_before_an_exact_match()
    {
        const string Source = "Start\tMarker\r\nkeep\nStart\r\nMarker\rtail\n";
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            Source,
            "Start\nMarker",
            "Changed",
            replaceAll: true,
            out var usedFlexible,
            out var matched,
            out var replacementCount,
            out _);

        Assert.True(matched);
        Assert.False(usedFlexible);
        Assert.Equal(1, replacementCount);
        Assert.Equal("Start\tMarker\r\nkeep\nChanged\rtail\n", updated);
    }

    [Fact]
    public void Match_starting_at_a_normalized_lf_consumes_the_full_crlf()
    {
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            "A\r\nB\nC",
            "\nB",
            string.Empty,
            replaceAll: false,
            out _,
            out _,
            out _,
            out _);

        Assert.Equal("A\nC", updated);
    }

    [Fact]
    public void A_normalized_noop_preserves_the_original_endings_inside_the_match()
    {
        const string Source = "keep\r\nStart\nMarker\rtail\n";
        var updated = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
            Source,
            "Start\nMarker",
            "Start\nMarker",
            replaceAll: false,
            out _,
            out var matched,
            out _,
            out _);

        Assert.True(matched);
        Assert.Equal(Source, updated);
    }

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
            out var replacementCount,
            out _);
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
            out var replacementCount,
            out _);

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
            out var replacementCount,
            out _);

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
            out var replacementCount,
            out _);

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
            out var replacementCount,
            out _);
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
            out var replacementCount,
            out _);

        Assert.False(matched);
        Assert.False(usedFlexible);
        Assert.Equal(0, replacementCount);
        Assert.Null(updated);
    }
}
