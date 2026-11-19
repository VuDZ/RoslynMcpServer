using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Il.Models;

internal sealed record IlFingerprintChanges(
    ImmutableArray<IlTypeName> Changed,
    ImmutableArray<IlTypeName> Added,
    ImmutableArray<IlTypeName> Removed,
    bool AssemblyChanged);
