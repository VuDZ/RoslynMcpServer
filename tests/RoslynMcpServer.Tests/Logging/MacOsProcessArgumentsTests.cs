using System.Buffers.Binary;
using System.Text;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Logging;

public sealed class MacOsProcessArgumentsTests
{
    [Theory]
    [InlineData(2, "/usr/local/bin/dotnet\0\0\0dotnet\0App With Spaces.dll\0SECRET=environment\0", "dotnet App With Spaces.dll")]
    [InlineData(3, "/usr/local/bin/dotnet\0dotnet\0\0App.dll\0", "dotnet  App.dll")]
    public void Parse_reads_argv_without_the_executable_prefix_or_environment(int count, string payload, string expected)
    {
        Assert.Equal(expected, MacOsProcessArguments.Parse(Buffer(count, payload)));
    }

    [Fact]
    public void Parse_does_not_identify_a_build_host_from_an_environment_value()
    {
        var payload = "/usr/local/bin/dotnet\0dotnet\0OtherApp.dll\0HOST="
            + "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll\0";

        Assert.Equal("dotnet OtherApp.dll", MacOsProcessArguments.Parse(Buffer(2, payload)));
    }

    [Theory]
    [InlineData(0, "dotnet\0dotnet\0")]
    [InlineData(-1, "dotnet\0dotnet\0")]
    [InlineData(int.MaxValue, "dotnet\0dotnet\0")]
    [InlineData(1, "dotnet")]
    [InlineData(1, "dotnet\0\0")]
    [InlineData(2, "dotnet\0dotnet\0App.dll")]
    [InlineData(3, "dotnet\0dotnet\0App.dll\0")]
    public void Parse_rejects_invalid_counts_and_incomplete_strings(int count, string payload)
    {
        Assert.Null(MacOsProcessArguments.Parse(Buffer(count, payload)));
    }

    [Fact]
    public void Parse_rejects_a_missing_or_incomplete_count()
    {
        Assert.Null(MacOsProcessArguments.Parse([]));
        Assert.Null(MacOsProcessArguments.Parse([1, 0, 0]));
    }

    [Fact]
    public void MacOS_reads_the_current_process_parent_and_arguments()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.True(MacOsParentProcess.TryGetParentProcessId(Environment.ProcessId) > 0);
        Assert.Contains("testhost", MacOsParentProcess.TryReadCommandLine(Environment.ProcessId));
        Assert.Equal(0, MacOsParentProcess.TryGetParentProcessId(int.MaxValue));
        Assert.Null(MacOsParentProcess.TryReadCommandLine(int.MaxValue));
    }

    private static byte[] Buffer(int count, string payload)
    {
        var bytes = new byte[sizeof(int) + Encoding.UTF8.GetByteCount(payload)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        Encoding.UTF8.GetBytes(payload, bytes.AsSpan(sizeof(int)));
        return bytes;
    }
}
