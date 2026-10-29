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
            var call = usage?.BeginTool(name, UsesWorkspace(descriptor, context.Params));
            var success = false;
            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                success = result.IsError != true && result.Content.OfType<TextContentBlock>().All(block => IsSuccessfulText(block.Text));
                return result;
            }
            finally
            {
                usage?.EndTool(call, success && !cancellationToken.IsCancellationRequested);
            }
        });
    }

    internal static bool UsesWorkspace(McpToolDescriptor? descriptor, CallToolRequestParams? parameters)
    {
        if (descriptor?.Group != McpToolGroups.Decompile)
        {
            return descriptor?.RequiresWorkspace ?? false;
        }

        var hasExplicitAssemblyPath = parameters?.Arguments is { } arguments
            && arguments.TryGetValue("assemblyPath", out var path)
            && path.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(path.GetString());
        return !hasExplicitAssemblyPath;
    }

    internal static bool IsSuccessfulText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var summary = ToolLogAnalyzer.BuildSummaryLine(text);
        var trimmed = text.TrimStart();
        return !summary.StartsWith("status=failed", StringComparison.Ordinal)
            && !summary.StartsWith("status=error", StringComparison.Ordinal)
            && !summary.StartsWith("status=partial", StringComparison.Ordinal)
            && !trimmed.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains("was cancelled", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains("cancelled by client", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains("Reset was cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.ToString() : null;
}
