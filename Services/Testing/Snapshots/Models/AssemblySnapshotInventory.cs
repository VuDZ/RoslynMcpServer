using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>What a session needs from a published solution to read its assemblies, without the solution itself.</summary>
/// <param name="OutputPaths">Distinct output paths of the loaded projects, in project order.</param>
/// <param name="AssemblyNames">
/// Simple names of the solution assemblies. Fingerprints write a reference to one of them without its
/// version; the set is fixed for the session, so two reads of one file always give equal fingerprints.
/// </param>
internal sealed record AssemblySnapshotInventory(ImmutableArray<string> OutputPaths, ImmutableHashSet<string> AssemblyNames);
