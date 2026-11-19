using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineMemberKey(
    SyntaxKind Kind,
    string ContainingTypePath,
    string Name,
    int Arity,
    string Parameters,
    string ExplicitInterface);
