using System.Collections.Concurrent;
using System.Reflection;
using ModelContextProtocol.Server;

namespace RoslynMcpServer.Hosting;

/// <summary>
/// SDK 1.3 <see cref="McpServerPrimitiveCollection{T}.TryAdd"/> raises <see cref="McpServerPrimitiveCollection{T}.Changed"/>
/// per tool. A group enablement must notify once, so this type batches inserts and raises a single event.
/// </summary>
public sealed class McpRuntimeToolCollection : McpServerPrimitiveCollection<McpServerTool>
{
    private static readonly FieldInfo PrimitivesField =
        typeof(McpServerPrimitiveCollection<McpServerTool>).GetField(
            "_primitives",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "ModelContextProtocol 1.3 McpServerPrimitiveCollection<T>._primitives was not found. "
            + "Update McpRuntimeToolCollection.TryAddMany for the installed SDK.");

    public int TryAddMany(IReadOnlyList<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0)
        {
            return 0;
        }

        var primitives = (ConcurrentDictionary<string, McpServerTool>)PrimitivesField.GetValue(this)!;
        var added = 0;
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (primitives.TryAdd(((IMcpServerPrimitive)tool).Id, tool))
            {
                added++;
            }
        }

        if (added > 0)
        {
            RaiseChanged();
        }

        return added;
    }
}
