using System.Diagnostics;
using System.Runtime.InteropServices;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

public sealed class DotNetCliRunnerEnvironmentTests
{
    [Theory]
    [InlineData(Architecture.X64, "AMD64")]
    [InlineData(Architecture.X86, "x86")]
    [InlineData(Architecture.Arm64, "ARM64")]
    [InlineData(Architecture.Arm, "ARM")]
    public void Missing_processor_architecture_is_filled_only_on_Windows(Architecture architecture, string expected)
    {
        var psi = new ProcessStartInfo();
        psi.Environment.Remove("PROCESSOR_ARCHITECTURE");

        DotNetCliRunner.EnsureWindowsProcessorArchitecture(psi, architecture);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(expected, psi.Environment["PROCESSOR_ARCHITECTURE"]);
        }
        else
        {
            Assert.False(psi.Environment.ContainsKey("PROCESSOR_ARCHITECTURE"));
        }
    }

    [Fact]
    public void Empty_processor_architecture_is_filled_only_on_Windows()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["PROCESSOR_ARCHITECTURE"] = string.Empty;

        DotNetCliRunner.EnsureWindowsProcessorArchitecture(psi, Architecture.X64);

        Assert.Equal(OperatingSystem.IsWindows() ? "AMD64" : string.Empty, psi.Environment["PROCESSOR_ARCHITECTURE"]);
    }

    [Theory]
    [InlineData("AMD64")]
    [InlineData("x86")]
    [InlineData("ARM64")]
    [InlineData("custom")]
    public void Existing_processor_architecture_is_preserved(string inheritedArchitecture)
    {
        var psi = new ProcessStartInfo();
        psi.Environment["PROCESSOR_ARCHITECTURE"] = inheritedArchitecture;

        DotNetCliRunner.EnsureWindowsProcessorArchitecture(psi, Architecture.X64);

        Assert.Equal(inheritedArchitecture, psi.Environment["PROCESSOR_ARCHITECTURE"]);
    }
}
