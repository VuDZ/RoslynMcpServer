using ModelContextProtocol.Server;

namespace RoslynMcpServer.Hosting;

/// <summary>
/// Batches tool inserts so one group enablement raises
/// <see cref="McpServerPrimitiveCollection{T}.Changed"/> once.
/// <see cref="McpServerPrimitiveCollection{T}.TryAdd"/> notifies on every insert; the SDK deferral
/// scope holds those notifications until the scope is disposed, and then raises at most one.
/// </summary>
public sealed class McpRuntimeToolCollection : McpServerPrimitiveCollection<McpServerTool>
{
    public int TryAddMany(IReadOnlyList<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0)
        {
            return 0;
        }

        var added = 0;
        using (DeferChangedEvents())
        {
            foreach (var tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool);
                if (TryAdd(tool))
                {
                    added++;
                }
            }
        }

        return added;
    }
}
