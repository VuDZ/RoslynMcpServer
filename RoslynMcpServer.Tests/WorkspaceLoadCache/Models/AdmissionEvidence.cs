using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record AdmissionEvidence(
    string ProfileVersion,
    string Disposition,
    ImmutableArray<EvidenceEntry> Entries,
    ImmutableArray<RegionEntry> Regions,
    ImmutableArray<InstanceKey> ExpectedInstances,
    ImmutableArray<ProjectEdge> ExpectedEdges,
    ImmutableArray<string> ExpectedCompilerInputPaths,
    ImmutableArray<string> Reasons,
    bool AnalyzerInventoryComplete,
    bool OverlayReady);
