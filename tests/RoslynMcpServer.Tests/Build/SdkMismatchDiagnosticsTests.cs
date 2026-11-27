using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class SdkMismatchDiagnosticsTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("\"", false)]
    [InlineData("", true)]
    [InlineData("\"", true)]
    public void Quoted_msbuild_path_uses_the_same_sdk_comparison_as_unquoted(string quotes, bool differentSdk)
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn mcp mismatch " + Guid.NewGuid().ToString("N"));
        var sdkDir = Path.Combine(root, "sdk", "10.0.999");
        Directory.CreateDirectory(Path.Combine(sdkDir, "Sdks"));
        File.WriteAllText(Path.Combine(sdkDir, "MSBuild.dll"), string.Empty);
        File.WriteAllText(Path.Combine(sdkDir, "Microsoft.Build.dll"), string.Empty);
        try
        {
            var sdk = DotNetSdkResolver.FromHostResult(
                Path.Combine(root, "global.json"), "10.0.301", "10.0.999", $"SDK path resolved to [{sdkDir}]");
            var logPath = Path.Combine(root, "sdk", differentSdk ? "9.0.314" : "10.0.999", "MSBuild.dll");
            var log = $"""
                    MSBuild executable path = {quotes}{logPath}{quotes}
                    Build FAILED.
                    """;
            Assert.Equal(differentSdk, DotNetBuildProbe.ShouldRunPinnedMsBuildRestore(log, sdk));

            var compilerLog = log + "\nCSC : error CS9057: Analyzer assembly references a newer compiler version.";
            var errors = SdkMismatchDiagnostics.CreateErrors(sdk, compilerLog);
            var metadata = DotNetCliRunner.BuildRunMetadata("dotnet", root, compilerLog, sdk);
            var report = BuildOutputReport.Format(
                compilerLog,
                1,
                metadata,
                includeBuildWarnings: false,
                additionalErrors: errors,
                attachFullReport: false);
            Assert.Contains("CS9057", report, StringComparison.Ordinal);
            Assert.Contains("- **dotnet --version:** `10.0.999`", report, StringComparison.Ordinal);
            Assert.Contains("- **Requested SDK:** `10.0.301`", report, StringComparison.Ordinal);
            Assert.Contains($"- **MSBuild from log:** `{logPath}`", report, StringComparison.Ordinal);
            Assert.Equal(differentSdk, report.Contains(SdkMismatchDiagnostics.MismatchCode, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unresolved_sdk_does_not_inject_a_mismatch()
    {
        var sdk = DotNetSdkResolver.FromHostResult("global.json", "10.0.999", null, null);
        Assert.Empty(SdkMismatchDiagnostics.CreateErrors(sdk, "MSBuild executable path = /sdk/10.0.402/MSBuild.dll"));
    }
}
