using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SkippedWorkspaceWriteDiskPolicyTests
{
    [Theory]
    [InlineData("no-workspace", true)]
    [InlineData("not-in-workspace", true)]
    [InlineData("missing-on-disk", false)]
    [InlineData("empty-path", false)]
    [InlineData(null, false)]
    public void ShouldWriteSkippedPathToDisk_only_for_unknown_snapshot_paths(string? reason, bool expected)
    {
        var write = WorkspaceWriteResult.Skipped(reason ?? "skipped");
        if (reason is null)
        {
            write = new WorkspaceWriteResult { Status = WorkspaceWriteStatus.Skipped };
        }

        Assert.Equal(expected, write.ShouldWriteSkippedPathToDisk);
    }

    [Fact]
    public void FullSuccess_does_not_use_skipped_disk_fallback()
    {
        var write = new WorkspaceWriteResult { Status = WorkspaceWriteStatus.FullSuccess };
        Assert.False(write.ShouldWriteSkippedPathToDisk);
    }
}
