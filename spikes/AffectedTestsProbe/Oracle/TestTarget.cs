namespace AffectedTestsProbe;

/// <summary>
/// One <c>dotnet test -f</c> invocation. Cases are mapped only to units of this path and framework.
/// A multi-targeted test project otherwise has two units for one method, and the name match is ambiguous.
/// </summary>
internal sealed record TestTarget(string ProjectPath, string TargetFramework, IReadOnlyList<TestUnit> Units)
{
    public static IReadOnlyList<TestTarget> Group(IEnumerable<TestUnit> units) =>
        units.GroupBy(unit => (unit.Project.ProjectPath, unit.Project.TargetFramework))
            .Select(group => new TestTarget(group.Key.ProjectPath, group.Key.TargetFramework, group.ToArray()))
            .ToArray();
}
