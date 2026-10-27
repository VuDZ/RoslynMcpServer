using System.Text.Json;

namespace BuildHostFork.Rpc.Models;

internal sealed record RpcProject(string Path, JsonElement Infos, JsonElement Diagnostics);
