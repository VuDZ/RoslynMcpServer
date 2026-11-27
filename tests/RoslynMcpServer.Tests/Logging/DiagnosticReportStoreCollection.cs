using Xunit;

namespace RoslynMcpServer.Tests.Logging;

/// <summary>
/// <see cref="DiagnosticReportStoreTests"/> and <see cref="DiagnosticReportAttachmentTests"/> share the
/// static report store and both clear it from the constructor and <see cref="IDisposable.Dispose"/>.
/// xUnit runs test classes in parallel, so one class's reset drops the other's cursor.
/// </summary>
[CollectionDefinition("DiagnosticReportStore", DisableParallelization = true)]
public sealed class DiagnosticReportStoreCollection
{
}
