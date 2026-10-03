using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Analyzers;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal delegate Task<AnalyzerProvenanceSnapshot?> ExperimentAnalyzerAdmission(
    SemanticSnapshot snapshot,
    AdmissionEvidence evidence,
    Guid newSessionId,
    Solution baseSolution,
    CancellationToken cancellationToken);
