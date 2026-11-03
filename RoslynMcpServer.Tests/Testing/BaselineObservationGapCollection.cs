using Xunit;

namespace RoslynMcpServer.Tests.Testing;

/// <summary>
/// Prevents parallel workspace loads from consuming the process-wide fail-next watcher startup flag.
/// </summary>
[CollectionDefinition("BaselineObservationGap", DisableParallelization = true)]
public sealed class BaselineObservationGapCollection
{
}
