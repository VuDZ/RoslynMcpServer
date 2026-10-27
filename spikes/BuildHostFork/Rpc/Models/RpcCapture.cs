using System.Text.Json;

namespace BuildHostFork.Rpc.Models;

internal sealed record RpcCapture(string HostPath, JsonElement MsBuild, RpcProject[] Projects);
