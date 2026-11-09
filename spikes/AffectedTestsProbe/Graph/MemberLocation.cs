using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>One source declaration of a member in one project key.</summary>
internal sealed record MemberLocation(ISymbol Symbol, MemberNode Node);
