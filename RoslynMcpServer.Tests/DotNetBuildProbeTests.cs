using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetBuildProbeTests
{
    [Fact]
    public void ShouldRunMoreDiagnostics_true_after_failed_build_with_no_parsed_errors()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1) ---
            Build FAILED.
                0 Warning(s)
                0 Error(s)
            """;
        Assert.True(DotNetBuildProbe.ShouldRunMoreDiagnostics(log));
    }

    [Fact]
    public void ShouldRunMoreDiagnostics_false_when_nuget_error_parsed()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1) ---
            error NU1904: Warning As Error: Package 'X' has a known critical severity vulnerability
            """;
        Assert.False(DotNetBuildProbe.ShouldRunMoreDiagnostics(log));
    }

    [Fact]
    public void ShouldRunMoreDiagnostics_true_after_restore_exit_zero_but_build_still_failed()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1, stdout 12 chars, stderr 0 chars) ---
            Build FAILED.
            --- dotnet restore -v:minimal (exit 0, stdout 40 chars, stderr 0 chars) ---
            All projects are up-to-date for restore.
            """;
        Assert.True(DotNetBuildProbe.ShouldRunMoreDiagnostics(log));
    }

    [Fact]
    public void ShouldRunPinnedMsBuildRestore_when_log_shows_wrong_msbuild_path()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1, stdout 10 chars, stderr 0 chars) ---
            MSBuild executable path = C:\Program Files\dotnet\sdk\9.0.314\MSBuild.dll
            Build FAILED.
            """;
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-probe-" + Guid.NewGuid().ToString("N"));
        var sdkDir = Path.Combine(root, "sdk", "10.0.888");
        Directory.CreateDirectory(Path.Combine(sdkDir, "Sdks"));
        File.WriteAllText(Path.Combine(sdkDir, "MSBuild.dll"), string.Empty);
        File.WriteAllText(Path.Combine(sdkDir, "Microsoft.Build.dll"), string.Empty);
        File.WriteAllText(Path.Combine(root, "global.json"), """{ "sdk": { "version": "10.0.888" } }""");
        lock (TestEnvironmentLocks.DotNetRoot)
        {
            var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);
            try
            {
                Assert.True(DotNetBuildProbe.ShouldRunPinnedMsBuildRestore(log, root));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ShouldRunDetailedRestore_when_exit_nonzero_and_output_empty()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1, stdout 0 chars, stderr 0 chars) ---
            Build FAILED.
            """;
        var restore = new DotNetCliRunner.RunResult(1, string.Empty, string.Empty, 0, 0);
        Assert.True(DotNetBuildProbe.ShouldRunDetailedRestore(log, restore));
    }

    [Fact]
    public void ComputeEffectiveBuildExitCode_restore_exit_zero_does_not_mask_failed_build()
    {
        // Classic false-success: build exit 1, then restore exit 0 becomes lastStepExitCode.
        var effective = DotNetBuildProbe.ComputeEffectiveBuildExitCode(
            buildExitCodes: [1],
            lastStepExitCode: 0,
            combinedLog: """
                --- dotnet build -v:minimal (exit 1, stdout 12 chars, stderr 0 chars) ---
                Build FAILED.
                --- dotnet restore -v:minimal (exit 0, stdout 40 chars, stderr 0 chars) ---
                All projects are up-to-date for restore.
                """);
        Assert.Equal(1, effective);
    }

    [Fact]
    public void ComputeEffectiveBuildExitCode_uses_last_build_after_escalate_recovery()
    {
        var effective = DotNetBuildProbe.ComputeEffectiveBuildExitCode(
            buildExitCodes: [1, 0],
            lastStepExitCode: 0,
            combinedLog: """
                --- dotnet build -v:minimal (exit 1) ---
                Build FAILED.
                --- dotnet restore -v:minimal (exit 0) ---
                --- dotnet build -v:normal (exit 0) ---
                Build succeeded.
                """);
        Assert.Equal(0, effective);
    }

    [Fact]
    public void ComputeEffectiveBuildExitCode_reads_failed_build_section_when_no_recorded_codes()
    {
        const string log = """
            --- dotnet build -v:minimal (exit 1, stdout 12 chars, stderr 0 chars) ---
            Build FAILED.
            --- dotnet restore -v:minimal (exit 0, stdout 40 chars, stderr 0 chars) ---
            All projects are up-to-date for restore.
            """;
        var effective = DotNetBuildProbe.ComputeEffectiveBuildExitCode(
            buildExitCodes: [],
            lastStepExitCode: 0,
            combinedLog: log);
        Assert.Equal(1, effective);
    }

    [Fact]
    public void FormatIncrementalSwitch_default_no_incremental()
    {
        Assert.Equal(" --no-incremental", DotNetBuildProbe.FormatIncrementalSwitch(noIncremental: true));
        Assert.Equal(string.Empty, DotNetBuildProbe.FormatIncrementalSwitch(noIncremental: false));
    }

    [Fact]
    public void TryGetFirstFailedBuildSectionExitCode_finds_nonzero_build_header()
    {
        const string log = """
            --- dotnet build -v:minimal --no-incremental (exit 1, stdout 10 chars, stderr 0 chars) ---
            Build FAILED.
            --- dotnet restore -v:minimal (exit 0) ---
            """;
        Assert.Equal(1, DotNetBuildProbe.TryGetFirstFailedBuildSectionExitCode(log));
    }
}
