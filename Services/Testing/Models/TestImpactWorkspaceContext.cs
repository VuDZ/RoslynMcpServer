using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>The published graph, change history and CLI options captured for one estimate or run.</summary>
internal sealed record TestImpactWorkspaceContext(
    Solution Solution,
    WorkspaceInputMap? InputMap,
    BaselineSessionSnapshot Baseline,
    string? WorkspacePath,
    bool ProjectGraphStale,
    bool ProjectGraphStaleFromGraphFile,
    string? Configuration,
    string? Platform,
    string? PlatformRaw,
    string? BuildArgs);
