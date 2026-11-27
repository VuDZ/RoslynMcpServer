using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>A null filter schedules the whole project; reasons explain selection and any conservative fallback.</summary>
internal sealed record VstestProjectRunPlan(ProjectId ProjectId, string? Filter, IReadOnlyList<string> Reasons);
