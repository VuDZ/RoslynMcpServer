namespace AffectedTestsProbe;

internal sealed class ImplementationIndex
{
    private readonly Dictionary<string, List<Implementation>> _byDocumentationId = new(StringComparer.Ordinal);
    private readonly HashSet<(string DocumentationId, MemberNode Node)> _seen = new();

    public void Add(string declaredDocumentationId, MemberNode implementation, string containingTypeKey)
    {
        if (!_seen.Add((declaredDocumentationId, implementation)))
        {
            return;
        }

        if (!_byDocumentationId.TryGetValue(declaredDocumentationId, out var list))
        {
            list = new List<Implementation>();
            _byDocumentationId.Add(declaredDocumentationId, list);
        }

        list.Add(new Implementation(implementation, containingTypeKey));
    }

    public IReadOnlyList<Implementation> Find(
        string declaredDocumentationId,
        ProjectKey caller,
        IReadOnlyDictionary<ProjectKey, HashSet<ProjectKey>> visible)
    {
        if (!_byDocumentationId.TryGetValue(declaredDocumentationId, out var list)
            || !visible.TryGetValue(caller, out var allowed))
        {
            return [];
        }

        return list.Where(item => allowed.Contains(item.Node.Project)).ToArray();
    }

    internal readonly record struct Implementation(MemberNode Node, string ContainingTypeKey);
}
