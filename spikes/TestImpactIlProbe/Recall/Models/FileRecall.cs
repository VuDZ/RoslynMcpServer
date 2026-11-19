namespace TestImpactIlProbe;

/// <summary>
/// One production file: how many test classes execute it and what each selector answers. A selector is level 0
/// or a layer, named as in the reports.
/// </summary>
/// <param name="ExecutingOutsideIdleRun">
/// Of the executing classes, those whose test assembly does not execute the file in its idle run. For the
/// others the report cannot tell the tests of the class from the search for tests that precedes them.
/// </param>
internal sealed record FileRecall(
    string Path,
    string Assembly,
    int Executing,
    int ExecutingOutsideIdleRun,
    int ExecutingMethods,
    IReadOnlyDictionary<string, SelectorRecall> Selectors);
