namespace AffectedTestsProbe;

/// <summary>A source member. Metadata symbols are leaves and have no node.</summary>
internal readonly record struct MemberNode(ProjectKey Project, string DocumentationId)
{
    public override string ToString() =>
        Project.ProjectPath + "|" + Project.TargetFramework + "|" + DocumentationId;
}
