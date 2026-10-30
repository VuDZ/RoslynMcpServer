using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

/// <summary>
/// <see cref="WorkspaceDiskWatcherStarterTests"/> and <see cref="WorkspaceInputFreshnessTests"/> set the static
/// <c>WorkspaceDiskWatcherStarter.FailNextStartForTests</c> seam. xUnit runs test classes in parallel, so a watcher
/// started by another class could consume the injected failure, or this class's watcher could fail for theirs.
/// </summary>
[CollectionDefinition("DiskWatcherStartSeam", DisableParallelization = true)]
public sealed class DiskWatcherStartSeamCollection
{
}
