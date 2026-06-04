using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SdkMismatchDiagnosticsTests
{
    [Fact]
    public void CreateErrors_reports_mismatch_when_log_msbuild_differs_from_pin()
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-mismatch-" + Guid.NewGuid().ToString("N"));
        var sdkDir = Path.Combine(root, "sdk", "10.0.999");
        Directory.CreateDirectory(Path.Combine(sdkDir, "Sdks"));
        File.WriteAllText(Path.Combine(sdkDir, "MSBuild.dll"), string.Empty);
        File.WriteAllText(Path.Combine(sdkDir, "Microsoft.Build.dll"), string.Empty);
        File.WriteAllText(
            Path.Combine(root, "global.json"),
            """
            { "sdk": { "version": "10.0.999" } }
            """);

        lock (TestEnvironmentLocks.DotNetRoot)
        {
            var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);
            try
            {
                const string log = """
                    MSBuild executable path = C:\Program Files\dotnet\sdk\9.0.314\MSBuild.dll
                    Build FAILED.
                    """;
                var errors = SdkMismatchDiagnostics.CreateErrors(root, log, "10.0.999");
                Assert.Contains(errors, e => e.Code == SdkMismatchDiagnostics.MismatchCode);
                Assert.Contains(errors, e => e.Message.Contains("9.0.314", StringComparison.Ordinal));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryParseDotNetVersionFromMetadata_reads_version_line()
    {
        const string metadata = "- **dotnet --version:** `10.0.204` (from working directory)";
        Assert.Equal("10.0.204", SdkMismatchDiagnostics.TryParseDotNetVersionFromMetadata(metadata));
    }
}
