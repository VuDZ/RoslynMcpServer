namespace AffectedTestsProbe;

/// <summary>Edges and creations extracted from one member. Lambdas stay inside this member.</summary>
internal sealed class MemberFact
{
    public List<CallSite> Calls { get; } = new();

    public HashSet<string> CreatedTypeKeys { get; } = new(StringComparer.Ordinal);

    public HashSet<string> MockedTypeKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Source types whose static constructor would run because this member uses them.</summary>
    public HashSet<(ProjectKey Project, string TypeDocumentationId)> StaticUses { get; } = new();
}
