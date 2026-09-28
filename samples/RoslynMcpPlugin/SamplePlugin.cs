using RoslynMcpServer.Plugins;

namespace RoslynMcpPlugin;

public sealed class SamplePlugin : IRoslynMcpPlugin
{
    public string Name => "sample";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<SampleTools>();
    }
}
