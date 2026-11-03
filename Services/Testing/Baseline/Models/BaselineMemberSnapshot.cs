namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineMemberSnapshot(BaselineMemberKey Key, string SyntaxHash, bool IsPartial);
