using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Skips Analyzer Lifecycle tests unless <c>ROSLYN_MCP_ANALYZER_LIFECYCLE=1</c>, and then skips
/// them again when the isolated MSBuild host cannot start. An unavailable environment must not be
/// reported as passed.
/// </summary>
internal sealed class AnalyzerLifecycleFactAttribute : FactAttribute
{
    public AnalyzerLifecycleFactAttribute()
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
