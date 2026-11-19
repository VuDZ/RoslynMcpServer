using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>A current test identity before adapter-specific name formatting.</summary>
internal sealed record AffectedTest(ProjectId ProjectId, BaselineMemberKey Key, IReadOnlyList<string> Reasons);
