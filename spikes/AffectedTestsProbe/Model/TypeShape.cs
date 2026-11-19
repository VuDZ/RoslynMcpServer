namespace AffectedTestsProbe;

/// <summary>
/// Constructors and initializers of one source type.
/// Initializers are linked from constructors after extraction, because the constructor body does not mention them.
/// </summary>
internal sealed class TypeShape
{
    public TypeShape(ProjectKey project, string typeDocumentationId)
    {
        Project = project;
        TypeDocumentationId = typeDocumentationId;
    }

    public ProjectKey Project { get; }

    public string TypeDocumentationId { get; }

    public bool IsRecord { get; set; }

    public MemberNode? Declaration { get; set; }

    public List<MemberNode> InstanceConstructors { get; } = new();

    public List<MemberNode> InstanceInitializers { get; } = new();

    public List<MemberNode> StaticInitializers { get; } = new();

    public List<MemberNode> Synthesized { get; } = new();

    public HashSet<MemberNode> ChainsToThis { get; } = new();
}
