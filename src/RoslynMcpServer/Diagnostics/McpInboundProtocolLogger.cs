using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Logs every incoming JSON-RPC message (before tool / handler dispatch) so you can correlate LM Studio traffic
/// with what the MCP host actually receives — including messages that fail later in the pipeline.
/// </summary>
public static class McpInboundProtocolLogger
{
    private const int DefaultMaxParamChars = 8000;

    /// <summary>
    /// Registers the outermost incoming message filter. Set env <c>MCP_LOG_INCOMING_RPC=0</c> to disable.
    /// <c>MCP_LOG_INCOMING_RPC_MAX_CHARS</c> caps logged params size (default 8000). Use <c>0</c> for no limit.
    /// </summary>
    public static void Register(McpServerOptions options)
    {
        if (!IsEnabled())
        {
            return;
        }

        var maxChars = GetMaxChars();
        options.Filters.Message.IncomingFilters.Insert(0, next => async (context, cancellationToken) =>
        {
            LogMessage(context.JsonRpcMessage, maxChars);
            await next(context, cancellationToken).ConfigureAwait(false);
        });
    }

    private static bool IsEnabled()
    {
        var v = Environment.GetEnvironmentVariable("MCP_LOG_INCOMING_RPC");
        if (string.IsNullOrEmpty(v))
        {
            return true;
        }

        return !string.Equals(v, "0", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMaxChars()
    {
        var v = Environment.GetEnvironmentVariable("MCP_LOG_INCOMING_RPC_MAX_CHARS");
        if (string.IsNullOrWhiteSpace(v))
        {
            return DefaultMaxParamChars;
        }

        if (int.TryParse(v, out var n))
        {
            return n <= 0 ? int.MaxValue : n;
        }

        return DefaultMaxParamChars;
    }

    private static void LogMessage(JsonRpcMessage message, int maxChars)
    {
        try
        {
            switch (message)
            {
                case JsonRpcRequest req:
                {
                    var id = req.Id.ToString();
                    var preview = FormatParams(req.Params, maxChars);
                    Log.Information(
                        "MCP ← request id={JsonRpcId} method={Method} params={ParamsPreview}",
                        id,
                        req.Method,
                        preview);
                    break;
                }
                case JsonRpcNotification n:
                {
                    var preview = FormatParams(n.Params, maxChars);
                    Log.Information(
                        "MCP ← notification method={Method} params={ParamsPreview}",
                        n.Method,
                        preview);
                    break;
                }
                default:
                    Log.Information(
                        "MCP ← message type={Type}",
                        message.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MCP ← failed to log inbound message");
        }
    }

    private static string FormatParams(JsonNode? paramsNode, int maxChars)
    {
        if (paramsNode is null)
        {
            return "(none)";
        }

        string text;
        try
        {
            text = paramsNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            text = paramsNode.ToString();
        }

        if (maxChars != int.MaxValue && text.Length > maxChars)
        {
            return $"{text.AsSpan(0, maxChars)}... [truncated {text.Length - maxChars} chars]";
        }

        return text;
    }
}
