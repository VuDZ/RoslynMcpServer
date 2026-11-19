using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineDocumentSnapshot(
    string TextChecksum,
    string ImportsAndNamespacesHash,
    string GlobalUsingsHash,
    string AssemblyAndModuleAttributesHash,
    string DirectivesHash,
    bool HasUnparseableContent,
    ImmutableArray<BaselineMemberSnapshot> Members)
{
    /// <summary>Base-list type names without qualification or generic arity, for conservative inheritance checks.</summary>
    internal ImmutableHashSet<string> BaseTypeNames { get; init; } = ImmutableHashSet<string>.Empty;
}
