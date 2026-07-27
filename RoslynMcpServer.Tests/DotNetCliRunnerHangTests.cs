using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetCliRunnerHangTests
{
    [Fact]
    public void FormatHangHints_mentions_timeout_and_zombie_dotnet()
    {
        var text = DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false);
        Assert.Contains("MCP_DOTNET_TIMEOUT", text, StringComparison.Ordinal);
        Assert.Contains("Get-Process dotnet", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithMetadataAsync_times_out_and_kills_long_running_process()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpHang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csproj = Path.Combine(root, "Hang.csproj");
            File.WriteAllText(csproj, OperatingSystem.IsWindows()
                ? """
                  <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                      <TargetFramework>net10.0</TargetFramework>
                    </PropertyGroup>
                    <Target Name="Build" />
                    <Target Name="CoreCompile" />
                    <Target Name="Hang" BeforeTargets="Build">
                      <Exec Command="ping -n 60 127.0.0.1" IgnoreExitCode="true" />
                    </Target>
                  </Project>
                  """
                : """
                  <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                      <TargetFramework>net10.0</TargetFramework>
                    </PropertyGroup>
                    <Target Name="Build" />
                    <Target Name="CoreCompile" />
                    <Target Name="Hang" BeforeTargets="Build">
                      <Exec Command="sleep 60" IgnoreExitCode="true" />
                    </Target>
                  </Project>
                  """);

            var run = await DotNetCliRunner.RunWithMetadataAsync(
                $"msbuild \"{csproj}\" /t:Hang /nologo /v:q",
                root,
                CancellationToken.None,
                TimeSpan.FromSeconds(4));

            Assert.True(run.TimedOut);
            Assert.True(run.ProcessKilled);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
