namespace AffectedTestsProbe;

/// <summary>
/// One edge from the member currently being extracted.
/// A metadata target keeps <see cref="LeafDocumentationId"/> and is not a node.
/// </summary>
internal readonly record struct CallSite(
    MemberNode? Direct,
    bool IsVirtual,
    string? VirtualDocumentationId,
    string? DeclaredTypeKey,
    string? LeafDocumentationId)
{
    public static CallSite To(MemberNode target) => new(target, false, null, null, null);
}
