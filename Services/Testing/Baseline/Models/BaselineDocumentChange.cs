namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineDocumentChange(
    BaselineChangeKind Kind,
    BaselineChangeReason Reason,
    BaselineMemberKey? Key = null,
    bool IsTestMethod = false,
    bool IsOwnTestClassRoot = false,
    bool BlocksNarrowing = false);
