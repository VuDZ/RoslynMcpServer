namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal sealed record BaselineMemberSnapshot(BaselineMemberKey Key, string SyntaxHash, bool IsPartial)
{
    internal bool IsTestMethod { get; init; }
    internal bool IsOwnTestClassRoot { get; init; }
    internal bool IsExtensionMethod { get; init; }
    /// <summary>Preserves test identity while forbidding test-only narrowing for inheritance barriers.</summary>
    internal bool BlocksNarrowing { get; init; }
    /// <summary>Syntax with initializers removed, used to keep accessor and declaration edits outside root-only narrowing.</summary>
    internal string? InitializerContextHash { get; init; }
}
