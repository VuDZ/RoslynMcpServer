using System.Text.Json;
using Xunit;

namespace RoslynMcpServer.Tests.Hosting;

/// <summary>
/// Workstation GC spent 40-46% of the first declaration search on Roslyn.slnx in pauses; server GC is
/// what the server is measured with. The generated runtimeconfig is the contract the runtime reads.
/// </summary>
public sealed class ServerGcConfigurationTests
{
    [Fact]
    public void Server_runtimeconfig_selects_server_gc()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoslynMcpServer.runtimeconfig.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var properties = document.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.True(
            properties.TryGetProperty("System.GC.Server", out var serverGc) && serverGc.GetBoolean(),
            $"{path} does not enable System.GC.Server.");
    }
}
