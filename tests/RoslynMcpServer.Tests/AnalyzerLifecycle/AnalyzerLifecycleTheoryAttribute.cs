using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Theory counterpart of <see cref="AnalyzerLifecycleFactAttribute"/>. No lifecycle theory
/// survives today; the type keeps the same gate should one be added back.
/// </summary>
internal sealed class AnalyzerLifecycleTheoryAttribute : TheoryAttribute
{
    public AnalyzerLifecycleTheoryAttribute()
    {
        var gate = AnalyzerLifecycleGate.GetSkipReason();
        if (gate is not null)
        {
            Skip = gate;
            return;
        }

        Skip = LifecycleEnvironment.GetUnavailableReason();
    }
}
