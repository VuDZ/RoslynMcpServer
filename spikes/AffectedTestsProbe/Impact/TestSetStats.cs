namespace AffectedTestsProbe;

/// <summary>Size of a test selection: test methods, runtime test classes, and test project keys.</summary>
internal readonly record struct TestSetStats(int Units, int Classes, int Projects)
{
    public static TestSetStats From(IReadOnlyCollection<TestUnit> units) => new(
        units.Count,
        units.Select(unit => (unit.Project, unit.RuntimeName)).Distinct().Count(),
        units.Select(unit => unit.Project).Distinct().Count());
}
