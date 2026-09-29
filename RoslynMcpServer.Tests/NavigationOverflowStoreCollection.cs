using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// <see cref="NavigationOverflowStoreTests"/> and <see cref="NavigationListingHelperTests"/> share the
/// static overflow store and both clear it from the constructor and <see cref="IDisposable.Dispose"/>.
/// xUnit runs test classes in parallel, so one class's reset drops the other's cursor.
/// </summary>
[CollectionDefinition("NavigationOverflowStore", DisableParallelization = true)]
public sealed class NavigationOverflowStoreCollection
{
}
