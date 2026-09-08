using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcpServer.Hosting;

/// <summary>One public MCP tool: catalog identity, host, group, and SDK factory.</summary>
public sealed class McpToolDescriptor
{
    public required string Name { get; init; }
    public required Type HostType { get; init; }
    public required MethodInfo Method { get; init; }
    public required string Group { get; init; }
    public required bool InLiteCore { get; init; }
    public required bool IsReadOnly { get; init; }

    public Func<IServiceProvider, McpServerTool> CreateFactory()
    {
        var method = Method;
        var hostType = HostType;
        if (method.IsStatic)
        {
            return services => McpServerTool.Create(
                method,
                (object?)null,
                new McpServerToolCreateOptions { Services = services });
        }

        return services => McpServerTool.Create(
            method,
            (RequestContext<CallToolRequestParams> request) =>
                ActivatorUtilities.CreateInstance(request.Services ?? services, hostType),
            new McpServerToolCreateOptions { Services = services });
    }
}
