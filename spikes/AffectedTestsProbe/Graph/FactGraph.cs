namespace AffectedTestsProbe;

internal sealed class FactGraph
{
    public Dictionary<MemberNode, MemberFact> Facts { get; } = new();

    public ImplementationIndex Implementations { get; } = new();

    public Dictionary<(ProjectKey Project, string TypeDocumentationId), TypeShape> Shapes { get; } = new();

    public MemberFact Fact(MemberNode node)
    {
        if (!Facts.TryGetValue(node, out var fact))
        {
            fact = new MemberFact();
            Facts.Add(node, fact);
        }

        return fact;
    }

    public TypeShape Shape(ProjectKey project, string typeDocumentationId)
    {
        var key = (project, typeDocumentationId);
        if (!Shapes.TryGetValue(key, out var shape))
        {
            shape = new TypeShape(project, typeDocumentationId);
            Shapes.Add(key, shape);
        }

        return shape;
    }
}
