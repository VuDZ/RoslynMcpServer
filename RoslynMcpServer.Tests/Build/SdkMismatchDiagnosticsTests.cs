using RoslynMcpServer.Diagnostics;
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
                var logPath = Path.Combine(root, "sdk", differentSdk ? "9.0.314" : "10.0.999", "MSBuild.dll");
                var log = $"""
                    MSBuild executable path = {quotes}{logPath}{quotes}
                    Build FAILED.
                    """;
                Assert.Equal(differentSdk, DotNetBuildProbe.ShouldRunPinnedMsBuildRestore(log, root));

                var compilerLog = log + "\nCSC : error CS9057: Analyzer assembly references a newer compiler version.";
                var errors = SdkMismatchDiagnostics.CreateErrors(root, compilerLog, "10.0.999");
                var metadata = DotNetCliRunner.BuildRunMetadata("dotnet", root, compilerLog, "10.0.999");
                var report = BuildOutputReport.Format(
                    compilerLog,
                    1,
                    metadata,
                    includeBuildWarnings: false,
                    additionalErrors: errors,
                    attachFullReport: false);
                Assert.Contains("CS9057", report, StringComparison.Ordinal);
                Assert.Contains("- **dotnet --version:** `10.0.999`", report, StringComparison.Ordinal);
                Assert.Contains($"- **MSBuild from log:** `{logPath}`", report, StringComparison.Ordinal);
                Assert.Equal(differentSdk, report.Contains(SdkMismatchDiagnostics.MismatchCode, StringComparison.Ordinal));
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
