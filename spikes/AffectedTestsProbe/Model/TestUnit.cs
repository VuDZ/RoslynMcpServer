namespace AffectedTestsProbe;

/// <summary>
/// One test method on one runtime class. An inherited method in two heirs is two units.
/// Parameterized rows collapse to this unit later; they are not separate units.
/// </summary>
internal sealed record TestUnit(ProjectKey Project, string RuntimeName, string MethodName, MemberNode Method)
{
    public string Key => string.Join(
        "|",
        Project.ProjectPath,
        Project.TargetFramework,
        RuntimeName,
        Method.DocumentationId);

    public override string ToString() => RuntimeName + "." + MethodName;
}
