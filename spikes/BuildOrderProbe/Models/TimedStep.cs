namespace BuildOrderProbe.Models;

/// <summary>
/// One root evaluation or one project's design-time builds (all its target frameworks) on the run's clock, in
/// milliseconds from the start of the run, with the managed thread that ran it. <see cref="MultiTargeting"/> marks a
/// project with TargetFrameworks and no TargetFramework, which the host builds once per target framework.
/// </summary>
internal sealed record TimedStep(string Path, bool MultiTargeting, double StartMs, double EndMs, int Thread)
{
    public double DurationMs => EndMs - StartMs;
}
