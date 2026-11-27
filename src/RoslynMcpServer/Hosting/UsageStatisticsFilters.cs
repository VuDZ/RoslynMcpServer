using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Diagnostics.UsageStatistics;

namespace RoslynMcpServer.Hosting;

internal static class UsageStatisticsFilters
{
    internal static void Register(McpServerOptions options)
    {
        options.Filters.Message.IncomingFilters.Insert(0, next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request)
            {
                try
                {
                    var usage = (context.Services ?? context.Server.Services)?.GetService<UsageStatisticsService>();
                    if (request.Method == "initialize")
                    {
                        usage?.RecordClient(
                            ReadString(request.Params?["clientInfo"]?["name"]),
                            ReadString(request.Params?["clientInfo"]?["version"]),
                            ReadString(request.Params?["protocolVersion"]));
                    }
                    else if (request.Context is { ClientInfo: { } client } requestContext)
                    {
                        // Since 2026-07-28 discovery replaces initialize; identity is authoritative per request.
                        usage?.RecordClient(client.Name, client.Version, requestContext.ProtocolVersion);
                    }
                }
                catch
                {
                    // Malformed client metadata belongs to the protocol handler, not telemetry.
                }
            }

            await next(context, cancellationToken).ConfigureAwait(false);
        });

        options.Filters.Request.CallToolFilters.Insert(0, next => async (context, cancellationToken) =>
        {
            var usage = (context.Services ?? context.Server.Services)?.GetService<UsageStatisticsService>();
            var name = context.Params?.Name ?? "unknown";
            var descriptor = McpToolCatalog.All.FirstOrDefault(tool => tool.Name == name);
            // See docs/usage-statistics/README.md: snapshot access does not widen the catalog classification.
            var call = usage?.BeginTool(name, descriptor?.RequiresWorkspace ?? false);
            var previousCall = ToolTelemetry.SetUsageCall(call);
            var success = false;
            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                // Response text may be source or process output that looks like an error.
                success = result.IsError != true && call?.Unsuccessful != true;
                return result;
            }
            finally
            {
                ToolTelemetry.SetUsageCall(previousCall);
                usage?.EndTool(call, success);
            }
        });
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.ToString() : null;
}
