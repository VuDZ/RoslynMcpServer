using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Config;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests.Support;

internal static class SolutionManagerTestFactory
{
    public static SolutionManager Create(
        RoslynMcpFileSettings? fileSettings = null,
        ILogger<SolutionManager>? logger = null)
    {
        var capture = new AnalyzerProvenanceCaptureService(
            NullLogger<AnalyzerProvenanceCaptureService>.Instance,
            Options.Create(new AnalyzerProvenanceCaptureOptions()),
            TimeProvider.System);
        // Background index prewarm stays off: it would add work and log lines to every loading test.
        // SearchIndexPrewarmTests turn it on.
        return new SolutionManager(logger ?? NullLogger<SolutionManager>.Instance, capture, fileSettings)
        {
            SearchPrewarmEnabled = false,
        };
    }
}
