using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpPlugin;

public sealed class SampleTools
{
    private readonly SolutionManager _solutions;

    public SampleTools(SolutionManager solutions)
    {
        _solutions = solutions;
    }

    [McpServerTool(Name = "sample_loaded_workspace", Title = "Loaded workspace path")]
    [Description("Returns the loaded workspace path, or says that none is loaded.")]
    public string LoadedWorkspace()
    {
        var path = _solutions.GetLoadedWorkspacePath();

        // Newtonsoft.Json is a private managed dependency of the plugin: the host shares only the
        // assemblies of its own contract, so this call is served by the Newtonsoft.Json.dll that lies
        // next to the entry assembly. The call is made for its side of the dependency contract, not for
        // its result, so the tool keeps returning the path itself; a JSON tree of a string property
        // round-trips to that same string.
        return string.IsNullOrEmpty(path)
            ? "No workspace is loaded."
            : JToken.FromObject(path).ToString();
    }
}
