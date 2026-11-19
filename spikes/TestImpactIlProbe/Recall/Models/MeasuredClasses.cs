namespace TestImpactIlProbe;

/// <summary>Which test classes the per-class coverage describes.</summary>
/// <param name="InGraph">Test classes of the graph.</param>
/// <param name="Measured">Of them, classes with a coverage report.</param>
/// <param name="ExecutingNothing">Of the measured, classes whose report holds no production file: skipped or failed before any code ran.</param>
/// <param name="UnknownReports">Reports for a class the graph does not have.</param>
/// <param name="TestAssemblies">Test assemblies of the measured classes.</param>
/// <param name="TestAssembliesWithIdleRun">
/// Of them, assemblies with a report of the idle run. The pairs of the others all count as outside the idle run.
/// </param>
/// <param name="TestAssembliesIdleRunExecutes">Of those, assemblies whose idle run executes at least one production file.</param>
internal sealed record MeasuredClasses(
    int InGraph,
    int Measured,
    int ExecutingNothing,
    int UnknownReports,
    int TestAssemblies,
    int TestAssembliesWithIdleRun,
    int TestAssembliesIdleRunExecutes);
