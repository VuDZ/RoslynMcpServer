namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineDocumentState(
    BaselineDocumentKey Key,
    BaselineDocumentStatus Status,
    BaselineDocumentSnapshot? Snapshot);
