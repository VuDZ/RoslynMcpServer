using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record DtbMeasurement(
    int OpenInvocations,
    int ProjectStarts,
    int CoreCompileTargets,
    int? DesignTimeContexts,
    bool Complete,
    ImmutableArray<string> EvidencePaths);
