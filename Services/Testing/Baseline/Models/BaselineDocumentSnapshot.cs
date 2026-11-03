using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineDocumentSnapshot(
    string TextChecksum,
    string ImportsAndNamespacesHash,
    string GlobalUsingsHash,
    string AssemblyAndModuleAttributesHash,
    string DirectivesHash,
    bool HasUnparseableContent,
    ImmutableArray<BaselineMemberSnapshot> Members);
