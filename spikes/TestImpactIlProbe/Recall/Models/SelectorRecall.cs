namespace TestImpactIlProbe;

/// <summary>
/// One answer for one file against the test classes that execute the file.
/// </summary>
/// <param name="Selected">Test classes in the answer.</param>
/// <param name="Kept">Test classes that execute the file and are in the answer.</param>
/// <param name="KeptMethods">Test methods of the kept classes.</param>
/// <param name="Lost">Test classes that execute the file and are missing from the answer.</param>
/// <param name="LostOutsideIdleRun">Of the lost, classes whose test assembly does not execute the file in its idle run.</param>
internal sealed record SelectorRecall(
    int Selected,
    int Kept,
    int KeptMethods,
    IReadOnlyList<int> Lost,
    IReadOnlyList<int> LostOutsideIdleRun);
