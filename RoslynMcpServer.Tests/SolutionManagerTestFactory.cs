using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests;

internal static class SolutionManagerTestFactory
{
    public static SolutionManager Create()
    {
        var capture = new AnalyzerProvenanceCaptureService(
            NullLogger<AnalyzerProvenanceCaptureService>.Instance,
            Options.Create(new AnalyzerProvenanceCaptureOptions()),
            TimeProvider.System);
        return new SolutionManager(NullLogger<SolutionManager>.Instance, capture);
    }
}
