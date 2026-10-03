using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record ProjectEntry(
    InstanceKey Key,
    string Name,
    string AssemblyName,
    string Language,
    string? OutputPath,
    string? OutputReferencePath,
    ImmutableSortedDictionary<string, string?> ParseOptions,
    ImmutableSortedDictionary<string, string?> CompilationOptions,
    ImmutableArray<DocumentEntry> Documents,
    ImmutableArray<ReferenceEntry> MetadataReferences,
    ImmutableArray<AnalyzerEntry> Analyzers);
