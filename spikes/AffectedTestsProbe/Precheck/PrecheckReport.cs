namespace AffectedTestsProbe;

internal sealed class PrecheckReport
{
    public PrecheckReport(bool passed, IReadOnlyList<string> failures)
    {
        Passed = passed;
        Failures = failures;
    }

    public bool Passed { get; }

    public IReadOnlyList<string> Failures { get; }
}
